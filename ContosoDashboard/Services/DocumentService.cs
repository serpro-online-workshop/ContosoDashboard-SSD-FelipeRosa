using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public sealed class DocumentService : IDocumentService
{
    private const long MaxFileSizeBytes = 26_214_400;
    private const int MaxPageSize = 100;
    private const int MaxTagCount = 30;
    private static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IMalwareScanner _scanner;
    private readonly INotificationService _notifications;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService storage,
        IMalwareScanner scanner,
        INotificationService notifications,
        ILogger<DocumentService> logger)
    {
        _context = context;
        _storage = storage;
        _scanner = scanner;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<DocumentOperationResult> UploadAsync(
        ClaimsPrincipal actor,
        DocumentUploadRequest request,
        Stream content,
        string originalFileName,
        string reportedContentType,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out _))
        {
            return Failure("unauthorized");
        }

        var metadataError = ValidateMetadata(request, originalFileName);
        if (metadataError is not null)
        {
            return Failure(metadataError);
        }

        var extension = Path.GetExtension(Path.GetFileName(originalFileName));
        if (!AllowedTypes.TryGetValue(extension, out var validatedContentType))
        {
            return Failure("unsupported-file-type");
        }

        var associations = await ValidateAssociationsAsync(actor, request.ProjectId, request.TaskId, cancellationToken);
        if (associations is null)
        {
            return Failure("project-or-task-access-denied");
        }

        StagedDocumentFile staged;
        try
        {
            staged = await _storage.StageAsync(content, userId, associations.Value.ProjectId, extension, cancellationToken, progress);
        }
        catch (InvalidDataException)
        {
            return Failure("file-size-or-content-invalid");
        }
        catch (IOException exception)
        {
            _logger.LogError(exception, "Document upload could not be staged.");
            return Failure("upload-storage-failed");
        }

        var keepStage = false;
        string? finalizedPath = null;
        try
        {
            if (!await IsValidFileContentAsync(_storage.GetAbsolutePath(staged.StagingRelativePath), extension, cancellationToken))
            {
                return Failure("file-content-invalid");
            }

            var scanResult = await _scanner.ScanAsync(_storage.GetAbsolutePath(staged.StagingRelativePath), cancellationToken);
            if (scanResult == MalwareScanResult.Infected)
            {
                return Failure("infected-file-rejected");
            }

            var scanOutcome = scanResult switch
            {
                MalwareScanResult.Clean => MalwareScanOutcome.Clean,
                MalwareScanResult.Unavailable => MalwareScanOutcome.Unavailable,
                _ => MalwareScanOutcome.Inconclusive
            };
            var scanWarning = scanOutcome != MalwareScanOutcome.Clean;
            if (scanWarning && !request.AllowUnscanned)
            {
                return new DocumentOperationResult(false, "unscanned-confirmation-required", ScanWarning: true);
            }

            finalizedPath = await _storage.FinalizeAsync(staged, cancellationToken);
            var document = new Document
            {
                Title = request.Title.Trim(),
                Description = NormalizeOptional(request.Description),
                Category = request.Category,
                OriginalFileName = Path.GetFileName(originalFileName),
                StoragePath = finalizedPath,
                FileType = validatedContentType,
                FileSizeBytes = staged.SizeBytes,
                UploadedAtUtc = DateTime.UtcNow,
                UploadedByUserId = userId,
                ProjectId = associations.Value.ProjectId,
                TaskId = request.TaskId,
                MalwareScanOutcome = scanOutcome
            };
            AddTags(document, request.Tags);
            _context.Documents.Add(document);
            _context.DocumentActivities.Add(CreateActivity(document, DocumentActivityType.Uploaded, userId));

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            if (document.ProjectId.HasValue)
            {
                await NotifyProjectMembersAsync(document, userId, "New Project Document", cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            keepStage = true;
            return new DocumentOperationResult(true, ScanWarning: scanWarning, DocumentId: document.DocumentId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Document upload failed while saving metadata.");
            return Failure("upload-failed");
        }
        finally
        {
            if (!keepStage)
            {
                if (finalizedPath is not null)
                {
                    await TryDeleteAsync(finalizedPath, CancellationToken.None);
                }
                else
                {
                    await TryDeleteAsync(staged.StagingRelativePath, CancellationToken.None);
                }
            }
            else
            {
                await TryDeleteAsync(staged.StagingRelativePath, CancellationToken.None);
            }
        }
    }

    public async Task<DocumentPageResult> SearchAsync(
        ClaimsPrincipal actor,
        DocumentSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return new DocumentPageResult([], 0, 1, 25);
        }

        var page = Math.Max(1, options.Page);
        var pageSize = Math.Clamp(options.PageSize, 1, MaxPageSize);
        var query = await BuildAccessibleQueryAsync(userId, role, cancellationToken);
        if (options.OwnedOnly)
        {
            query = query.Where(document => document.UploadedByUserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(options.SearchText))
        {
            var term = options.SearchText.Trim();
            query = query.Where(document => document.Title.Contains(term) ||
                (document.Description != null && document.Description.Contains(term)) ||
                document.Tags.Any(tag => tag.Tag.Contains(term)) ||
                document.UploadedByUser.DisplayName.Contains(term) ||
                (document.Project != null && document.Project.Name.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(options.Category))
        {
            query = query.Where(document => document.Category == options.Category);
        }
        if (options.ProjectId.HasValue)
        {
            query = query.Where(document => document.ProjectId == options.ProjectId.Value);
        }
        if (options.UploadedFrom.HasValue)
        {
            var from = options.UploadedFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(document => document.UploadedAtUtc >= from);
        }
        if (options.UploadedThrough.HasValue)
        {
            var through = options.UploadedThrough.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(document => document.UploadedAtUtc < through);
        }

        query = ApplySort(query, options.SortBy, options.Descending);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(ToListItem())
            .ToListAsync(cancellationToken);
        return new DocumentPageResult(items, totalCount, page, pageSize);
    }

    public async Task<Document?> GetAccessibleDocumentAsync(
        ClaimsPrincipal actor,
        int documentId,
        bool preview = false,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return null;
        }

        var query = await BuildAccessibleQueryAsync(userId, role, cancellationToken);
        var document = await query.FirstOrDefaultAsync(item => item.DocumentId == documentId, cancellationToken);
        if (preview && document is not null && document.FileType is not ("application/pdf" or "image/jpeg" or "image/png"))
        {
            return null;
        }

        return document;
    }

    public async Task RecordDownloadAsync(ClaimsPrincipal actor, Document document, CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return;
        }

        var accessible = await GetAccessibleDocumentAsync(actor, document.DocumentId, cancellationToken: cancellationToken);
        if (accessible is null)
        {
            return;
        }

        _context.DocumentActivities.Add(CreateActivity(accessible, DocumentActivityType.Downloaded, userId));
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateMetadataAsync(
        ClaimsPrincipal actor,
        int documentId,
        string title,
        string? description,
        string category,
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return false;
        }

        var document = await _context.Documents.Include(item => item.Tags)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId, cancellationToken);
        if (document is null || !await CanManageDocumentAsync(userId, role, document, cancellationToken) ||
            !IsValidMetadata(title, description, category, tags))
        {
            return false;
        }

        document.Title = title.Trim();
        document.Description = NormalizeOptional(description);
        document.Category = category;
        _context.DocumentTags.RemoveRange(document.Tags);
        document.Tags.Clear();
        AddTags(document, tags);
        _context.DocumentActivities.Add(CreateActivity(document, DocumentActivityType.MetadataUpdated, userId));
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<DocumentOperationResult> ReplaceFileAsync(
        ClaimsPrincipal actor,
        int documentId,
        Stream content,
        string originalFileName,
        string reportedContentType,
        bool allowUnscanned = false,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return Failure("unauthorized");
        }

        var document = await _context.Documents.FirstOrDefaultAsync(item => item.DocumentId == documentId, cancellationToken);
        if (document is null || !await CanManageDocumentAsync(userId, role, document, cancellationToken))
        {
            return Failure("document-not-found-or-forbidden");
        }

        var extension = Path.GetExtension(Path.GetFileName(originalFileName));
        if (!AllowedTypes.TryGetValue(extension, out var validatedContentType) || originalFileName.Length > 255)
        {
            return Failure("unsupported-file-type");
        }

        StagedDocumentFile staged;
        try
        {
            staged = await _storage.StageAsync(content, userId, document.ProjectId, extension, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Failure("file-size-or-content-invalid");
        }

        string? newPath = null;
        try
        {
            if (!await IsValidFileContentAsync(_storage.GetAbsolutePath(staged.StagingRelativePath), extension, cancellationToken))
            {
                return Failure("file-content-invalid");
            }

            var scan = await _scanner.ScanAsync(_storage.GetAbsolutePath(staged.StagingRelativePath), cancellationToken);
            if (scan == MalwareScanResult.Infected)
            {
                return Failure("infected-file-rejected");
            }
            var scanWarning = scan != MalwareScanResult.Clean;
            if (scanWarning && !allowUnscanned)
            {
                return new DocumentOperationResult(false, "unscanned-confirmation-required", ScanWarning: true);
            }

            var outcome = scan switch
            {
                MalwareScanResult.Clean => MalwareScanOutcome.Clean,
                MalwareScanResult.Unavailable => MalwareScanOutcome.Unavailable,
                _ => MalwareScanOutcome.Inconclusive
            };
            newPath = await _storage.FinalizeAsync(staged, cancellationToken);
            var oldPath = document.StoragePath;
            document.StoragePath = newPath;
            document.OriginalFileName = Path.GetFileName(originalFileName);
            document.FileType = validatedContentType;
            document.FileSizeBytes = staged.SizeBytes;
            document.UploadedAtUtc = DateTime.UtcNow;
            document.MalwareScanOutcome = outcome;
            _context.DocumentActivities.Add(CreateActivity(document, DocumentActivityType.Replaced, userId));
            await _context.SaveChangesAsync(cancellationToken);
            await TryDeleteAsync(oldPath, cancellationToken);
            return new DocumentOperationResult(true, ScanWarning: scanWarning, DocumentId: documentId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Document replacement failed.");
            return Failure("replacement-failed");
        }
        finally
        {
            if (newPath is not null && document.StoragePath != newPath)
            {
                await TryDeleteAsync(newPath, CancellationToken.None);
            }
            await TryDeleteAsync(staged.StagingRelativePath, CancellationToken.None);
        }
    }

    public async Task<bool> DeleteAsync(ClaimsPrincipal actor, int documentId, CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return false;
        }

        var document = await _context.Documents.Include(item => item.Tags).Include(item => item.Shares)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId, cancellationToken);
        if (document is null || !await CanManageDocumentAsync(userId, role, document, cancellationToken))
        {
            return false;
        }

        var oldPath = document.StoragePath;
        _context.DocumentActivities.Add(CreateActivity(document, DocumentActivityType.Deleted, userId));
        _context.Documents.Remove(document);
        await _context.SaveChangesAsync(cancellationToken);
        await TryDeleteAsync(oldPath, cancellationToken);
        return true;
    }

    public async Task<bool> ShareAsync(
        ClaimsPrincipal actor,
        int documentId,
        DocumentShareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return false;
        }

        var hasUser = request.RecipientUserId.HasValue;
        var hasDepartment = !string.IsNullOrWhiteSpace(request.RecipientDepartment);
        if (hasUser == hasDepartment || (hasDepartment && request.RecipientDepartment!.Length > 100))
        {
            return false;
        }

        var document = await _context.Documents.Include(item => item.UploadedByUser)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId, cancellationToken);
        if (document is null || (document.UploadedByUserId != userId && role != UserRole.Administrator))
        {
            return false;
        }

        User? recipient = null;
        if (hasUser)
        {
            recipient = await _context.Users.FindAsync([request.RecipientUserId!.Value], cancellationToken);
            if (recipient is null || recipient.UserId == userId)
            {
                return false;
            }
        }

        var department = hasDepartment ? request.RecipientDepartment!.Trim() : null;
        if (hasDepartment && !await _context.Users.AnyAsync(user => user.Department == department, cancellationToken))
        {
            return false;
        }

        var exists = await _context.DocumentShares.AnyAsync(share =>
            share.DocumentId == documentId && share.RevokedAtUtc == null &&
            (hasUser ? share.RecipientUserId == recipient!.UserId : share.RecipientDepartment == department), cancellationToken);
        if (exists)
        {
            return false;
        }

        var share = new DocumentShare
        {
            DocumentId = documentId,
            RecipientUserId = recipient?.UserId,
            RecipientDepartment = department,
            GrantedByUserId = userId,
            GrantedAtUtc = DateTime.UtcNow
        };
        _context.DocumentShares.Add(share);
        _context.DocumentActivities.Add(CreateActivity(document, DocumentActivityType.Shared, userId,
            recipient?.DisplayName ?? department));
        await _context.SaveChangesAsync(cancellationToken);

        if (recipient is not null)
        {
            await NotifyUserAsync(recipient.UserId, "Document shared with you", $"A document was shared with you: {document.Title}", NotificationType.DocumentShared, cancellationToken);
        }
        else
        {
            var recipients = await _context.Users.Where(user => user.Department == department && user.UserId != userId && user.InAppNotificationsEnabled)
                .Select(user => user.UserId).ToListAsync(cancellationToken);
            foreach (var recipientId in recipients)
            {
                await NotifyUserAsync(recipientId, "Document shared with your team", $"A document was shared with your department: {document.Title}", NotificationType.DocumentShared, cancellationToken);
            }
        }
        return true;
    }

    public async Task<bool> RevokeShareAsync(
        ClaimsPrincipal actor,
        int documentId,
        int shareId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return false;
        }

        var share = await _context.DocumentShares.Include(item => item.Document)
            .FirstOrDefaultAsync(item => item.DocumentShareId == shareId && item.DocumentId == documentId, cancellationToken);
        if (share is null || (share.Document.UploadedByUserId != userId && role != UserRole.Administrator))
        {
            return false;
        }

        if (share.RevokedAtUtc.HasValue)
        {
            return false;
        }

        share.RevokedAtUtc = DateTime.UtcNow;
        share.RevokedByUserId = userId;
        _context.DocumentActivities.Add(CreateActivity(share.Document, DocumentActivityType.ShareRevoked, userId,
            share.RecipientUserId?.ToString(CultureInfo.InvariantCulture) ?? share.RecipientDepartment));
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<DocumentListItem>> GetSharedWithMeAsync(
        ClaimsPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return [];
        }

        var department = await _context.Users.Where(user => user.UserId == userId).Select(user => user.Department).FirstOrDefaultAsync(cancellationToken);
        var query = await BuildAccessibleQueryAsync(userId, role, cancellationToken);
        query = query.Where(document => document.Shares.Any(share => share.RevokedAtUtc == null &&
            (share.RecipientUserId == userId || (department != null && share.RecipientDepartment == department))));
        return await query.OrderByDescending(document => document.UploadedAtUtc).Select(ToListItem()).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentShare>> GetDocumentSharesAsync(
        ClaimsPrincipal actor,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return [];
        }

        var documentOwnerId = await _context.Documents.Where(document => document.DocumentId == documentId)
            .Select(document => (int?)document.UploadedByUserId).FirstOrDefaultAsync(cancellationToken);
        if (!documentOwnerId.HasValue || (documentOwnerId.Value != userId && role != UserRole.Administrator))
        {
            return [];
        }

        return await _context.DocumentShares.AsNoTracking()
            .Include(share => share.RecipientUser)
            .Where(share => share.DocumentId == documentId && share.RevokedAtUtc == null)
            .OrderBy(share => share.GrantedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentListItem>> GetProjectDocumentsAsync(
        ClaimsPrincipal actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return [];
        }

        var query = await BuildAccessibleQueryAsync(userId, role, cancellationToken);
        return await query.Where(document => document.ProjectId == projectId)
            .OrderByDescending(document => document.UploadedAtUtc).Select(ToListItem()).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentListItem>> GetTaskDocumentsAsync(
        ClaimsPrincipal actor,
        int taskId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role) || !await CanAccessTaskAsync(userId, role, taskId, cancellationToken))
        {
            return [];
        }

        var query = await BuildAccessibleQueryAsync(userId, role, cancellationToken);
        return await query.Where(document => document.TaskId == taskId)
            .OrderByDescending(document => document.UploadedAtUtc).Select(ToListItem()).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentListItem>> GetAttachableDocumentsAsync(
        ClaimsPrincipal actor,
        int taskId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role) || !await CanAccessTaskAsync(userId, role, taskId, cancellationToken))
        {
            return [];
        }

        var projectId = await _context.Tasks.Where(task => task.TaskId == taskId)
            .Select(task => task.ProjectId).FirstOrDefaultAsync(cancellationToken);
        var query = await BuildAccessibleQueryAsync(userId, role, cancellationToken);
        var candidates = await query.Where(document => document.TaskId == null &&
                (document.ProjectId == null || document.ProjectId == projectId))
            .OrderByDescending(document => document.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        var manageable = new List<Document>();
        foreach (var document in candidates)
        {
            if (await CanManageDocumentAsync(userId, role, document, cancellationToken))
            {
                manageable.Add(document);
            }
        }

        return manageable.Select(document => new DocumentListItem(
            document.DocumentId, document.Title, document.Category, document.UploadedAtUtc,
            document.FileSizeBytes, document.Project?.Name, document.UploadedByUser.DisplayName,
            document.FileType, document.MalwareScanOutcome)).ToList();
    }

    public async Task<bool> AttachToTaskAsync(
        ClaimsPrincipal actor,
        int documentId,
        int taskId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out var userId, out var role) || !await CanAccessTaskAsync(userId, role, taskId, cancellationToken))
        {
            return false;
        }

        var taskProjectId = await _context.Tasks.Where(task => task.TaskId == taskId)
            .Select(task => task.ProjectId).FirstOrDefaultAsync(cancellationToken);
        var document = await _context.Documents.Include(item => item.Project)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId, cancellationToken);
        if (document is null || document.TaskId.HasValue || !await CanManageDocumentAsync(userId, role, document, cancellationToken) ||
            (document.ProjectId.HasValue && document.ProjectId != taskProjectId))
        {
            return false;
        }

        document.TaskId = taskId;
        document.ProjectId ??= taskProjectId;
        _context.DocumentActivities.Add(CreateActivity(document, DocumentActivityType.TaskAttached, userId));
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<DocumentReportResult?> GetReportsAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(actor, out _, out var role) || role != UserRole.Administrator)
        {
            return null;
        }

        var fileTypeRows = await _context.Documents.AsNoTracking()
            .GroupBy(document => document.FileType)
            .Select(group => new { FileType = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count).ToListAsync(cancellationToken);
        var fileTypes = fileTypeRows.Select(item => new DocumentTypeReportItem(item.FileType, item.Count)).ToList();
        var uploaderRows = await _context.Documents.AsNoTracking()
            .GroupBy(document => new { document.UploadedByUserId, document.UploadedByUser.DisplayName })
            .Select(group => new { group.Key.UploadedByUserId, group.Key.DisplayName, Count = group.Count() })
            .OrderByDescending(item => item.Count).ToListAsync(cancellationToken);
        var uploaders = uploaderRows.Select(item => new DocumentUploaderReportItem(item.UploadedByUserId, item.DisplayName, item.Count)).ToList();
        var activityRows = await _context.DocumentActivities.AsNoTracking()
            .GroupBy(activity => activity.ActivityType)
            .Select(group => new { ActivityType = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var activities = activityRows.Select(item => new DocumentAccessReportItem(item.ActivityType, item.Count)).ToList();
        return new DocumentReportResult(fileTypes, uploaders, activities);
    }

    private async Task<IQueryable<Document>> BuildAccessibleQueryAsync(int userId, UserRole role, CancellationToken cancellationToken)
    {
        if (role == UserRole.Administrator)
        {
            return _context.Documents.AsNoTracking().Include(document => document.UploadedByUser).Include(document => document.Project)
                .Include(document => document.Tags).AsQueryable();
        }

        var department = await _context.Users.Where(user => user.UserId == userId).Select(user => user.Department).FirstOrDefaultAsync(cancellationToken);
        var includeDepartmentDocuments = role == UserRole.TeamLead && department is not null;
        var query = _context.Documents.AsNoTracking()
            .Include(document => document.UploadedByUser)
            .Include(document => document.Project)
            .Include(document => document.Tags)
            .Where(document => document.UploadedByUserId == userId ||
                (includeDepartmentDocuments && document.UploadedByUser.Department == department) ||
                document.Shares.Any(share => share.RevokedAtUtc == null &&
                    (share.RecipientUserId == userId || (department != null && share.RecipientDepartment == department))) ||
                (document.Project != null && (document.Project.ProjectManagerId == userId ||
                    document.Project.ProjectMembers.Any(member => member.UserId == userId))) ||
                (document.Task != null && (document.Task.AssignedUserId == userId || document.Task.CreatedByUserId == userId)));

        return query;
    }

    private async Task<bool> CanManageDocumentAsync(int userId, UserRole role, Document document, CancellationToken cancellationToken)
    {
        if (role == UserRole.Administrator || document.UploadedByUserId == userId)
        {
            return true;
        }

        if (role == UserRole.ProjectManager && document.ProjectId.HasValue &&
            await _context.Projects.AnyAsync(project => project.ProjectId == document.ProjectId && project.ProjectManagerId == userId, cancellationToken))
        {
            return true;
        }

        if (role == UserRole.TeamLead)
        {
            var department = await _context.Users.Where(user => user.UserId == userId).Select(user => user.Department).FirstOrDefaultAsync(cancellationToken);
            if (department is not null && await _context.Users.AnyAsync(user => user.UserId == document.UploadedByUserId && user.Department == department, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<(int? ProjectId, string? Error)?> ValidateAssociationsAsync(
        ClaimsPrincipal actor,
        int? projectId,
        int? taskId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(actor, out var userId, out var role))
        {
            return null;
        }

        if (projectId.HasValue)
        {
            var projectAccess = role == UserRole.Administrator || await _context.Projects.AnyAsync(project =>
                project.ProjectId == projectId && (project.ProjectManagerId == userId || project.ProjectMembers.Any(member => member.UserId == userId)), cancellationToken);
            if (!projectAccess)
            {
                return null;
            }
        }

        if (taskId.HasValue)
        {
            var task = await _context.Tasks.AsNoTracking().Where(item => item.TaskId == taskId.Value)
                .Select(item => new { item.ProjectId, item.AssignedUserId, item.CreatedByUserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (task is null || !await CanAccessTaskAsync(userId, role, taskId.Value, cancellationToken))
            {
                return null;
            }
            if (projectId.HasValue && projectId != task.ProjectId)
            {
                return null;
            }
            projectId ??= task.ProjectId;
        }

        return (projectId, null);
    }

    private async Task<bool> CanAccessTaskAsync(int userId, UserRole role, int taskId, CancellationToken cancellationToken)
    {
        if (role == UserRole.Administrator)
        {
            return await _context.Tasks.AnyAsync(task => task.TaskId == taskId, cancellationToken);
        }

        return await _context.Tasks.AnyAsync(task => task.TaskId == taskId &&
            (task.AssignedUserId == userId || task.CreatedByUserId == userId ||
             (task.Project != null && (task.Project.ProjectManagerId == userId || task.Project.ProjectMembers.Any(member => member.UserId == userId)))), cancellationToken);
    }

    private async Task<bool> IsProjectManagerAsync(int userId, int? projectId, CancellationToken cancellationToken)
    {
        return projectId.HasValue && await _context.Projects.AnyAsync(project => project.ProjectId == projectId && project.ProjectManagerId == userId, cancellationToken);
    }

    private async Task NotifyProjectMembersAsync(Document document, int actorId, string title, CancellationToken cancellationToken)
    {
        if (!document.ProjectId.HasValue)
        {
            return;
        }

        var recipients = await _context.ProjectMembers.Where(member => member.ProjectId == document.ProjectId &&
                member.UserId != actorId && member.User.InAppNotificationsEnabled)
            .Select(member => member.UserId).Distinct().ToListAsync(cancellationToken);
        var projectManagerId = await _context.Projects.Where(project => project.ProjectId == document.ProjectId)
            .Select(project => project.ProjectManagerId).FirstOrDefaultAsync(cancellationToken);
        if (projectManagerId != actorId && projectManagerId > 0 &&
            await _context.Users.AnyAsync(user => user.UserId == projectManagerId && user.InAppNotificationsEnabled, cancellationToken))
        {
            recipients.Add(projectManagerId);
        }

        foreach (var recipientId in recipients.Distinct())
        {
            await NotifyUserAsync(recipientId, title, $"A document was added to a project: {document.Title}", NotificationType.DocumentAddedToProject, cancellationToken);
        }
    }

    private async Task NotifyUserAsync(int userId, string title, string message, NotificationType type, CancellationToken cancellationToken)
    {
        var enabled = await _context.Users.Where(user => user.UserId == userId).Select(user => user.InAppNotificationsEnabled).FirstOrDefaultAsync(cancellationToken);
        if (enabled)
        {
            await _notifications.CreateNotificationAsync(new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                Priority = NotificationPriority.Informational
            });
        }
    }

    private async Task TryDeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _storage.DeleteAsync(path, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Private document cleanup failed; path is no longer exposed by document metadata.");
        }
    }

    private static IQueryable<Document> ApplySort(IQueryable<Document> query, DocumentSortField sortBy, bool descending)
    {
        return (sortBy, descending) switch
        {
            (DocumentSortField.Title, false) => query.OrderBy(document => document.Title).ThenBy(document => document.DocumentId),
            (DocumentSortField.Title, true) => query.OrderByDescending(document => document.Title).ThenByDescending(document => document.DocumentId),
            (DocumentSortField.Category, false) => query.OrderBy(document => document.Category).ThenBy(document => document.DocumentId),
            (DocumentSortField.Category, true) => query.OrderByDescending(document => document.Category).ThenByDescending(document => document.DocumentId),
            (DocumentSortField.FileSize, false) => query.OrderBy(document => document.FileSizeBytes).ThenBy(document => document.DocumentId),
            (DocumentSortField.FileSize, true) => query.OrderByDescending(document => document.FileSizeBytes).ThenByDescending(document => document.DocumentId),
            (DocumentSortField.UploadedAt, false) => query.OrderBy(document => document.UploadedAtUtc).ThenBy(document => document.DocumentId),
            _ => query.OrderByDescending(document => document.UploadedAtUtc).ThenByDescending(document => document.DocumentId)
        };
    }

    private static System.Linq.Expressions.Expression<Func<Document, DocumentListItem>> ToListItem() => document =>
        new DocumentListItem(document.DocumentId, document.Title, document.Category, document.UploadedAtUtc,
            document.FileSizeBytes, document.Project == null ? null : document.Project.Name,
            document.UploadedByUser.DisplayName, document.FileType, document.MalwareScanOutcome);

    private static DocumentActivity CreateActivity(Document document, DocumentActivityType activityType, int actorId, string? target = null) => new()
    {
        DocumentId = document.DocumentId,
        DocumentTitleSnapshot = document.Title,
        ActivityType = activityType,
        ActorUserId = actorId,
        OccurredAtUtc = DateTime.UtcNow,
        TargetSnapshot = target
    };

    private static void AddTags(Document document, IEnumerable<string> tags)
    {
        foreach (var tag in NormalizeTags(tags))
        {
            document.Tags.Add(new DocumentTag { Tag = tag });
        }
    }

    private static IReadOnlyCollection<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        return tags.Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTagCount + 1)
            .ToArray();
    }

    private static string? ValidateMetadata(DocumentUploadRequest request, string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName) || originalFileName.Length > 255 ||
            Path.GetFileName(originalFileName) != originalFileName)
        {
            return "invalid-file-name";
        }
        if (!IsValidMetadata(request.Title, request.Description, request.Category, request.Tags))
        {
            return "invalid-metadata";
        }
        return null;
    }

    private static bool IsValidMetadata(string title, string? description, string category, IEnumerable<string>? tags)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 255 ||
            (description?.Length ?? 0) > 2000 || !DocumentCategories.Allowed.Contains(category, StringComparer.Ordinal))
        {
            return false;
        }

        var normalizedTags = NormalizeTags(tags);
        return normalizedTags.Count <= MaxTagCount && normalizedTags.All(tag => tag.Length <= 50);
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryGetActor(ClaimsPrincipal principal, out int userId, out UserRole role)
    {
        userId = 0;
        role = UserRole.Employee;
        if (principal.Identity?.IsAuthenticated != true ||
            !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId))
        {
            return false;
        }

        var roleValue = principal.FindFirstValue(ClaimTypes.Role);
        if (!Enum.TryParse(roleValue, ignoreCase: false, out role))
        {
            userId = 0;
            return false;
        }

        return true;
    }

    private static DocumentOperationResult Failure(string code) => new(false, ErrorCode: code);

    private static async Task<bool> IsValidFileContentAsync(string path, string extension, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = new byte[8];
        var read = await stream.ReadAsync(header.AsMemory(), cancellationToken);
        if (read == 0)
        {
            return false;
        }

        switch (extension.ToLowerInvariant())
        {
            case ".pdf":
                return read >= 5 && Encoding.ASCII.GetString(header, 0, 5) == "%PDF-";
            case ".png":
                return read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            case ".jpg":
            case ".jpeg":
                return read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            case ".txt":
                stream.Position = 0;
                var buffer = new byte[Math.Min((int)stream.Length, 8192)];
                var length = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
                return !buffer.AsSpan(0, length).Contains((byte)0) && !HasDisallowedTextControl(buffer.AsSpan(0, length));
            case ".docx":
                return await HasOfficeEntryAsync(stream, "word/document.xml", cancellationToken);
            case ".xlsx":
                return await HasOfficeEntryAsync(stream, "xl/workbook.xml", cancellationToken);
            case ".pptx":
                return await HasOfficeEntryAsync(stream, "ppt/presentation.xml", cancellationToken);
            default:
                return false;
        }
    }

    private static async Task<bool> HasOfficeEntryAsync(Stream stream, string requiredEntry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stream.Length < 4)
        {
            return false;
        }

        stream.Position = 0;
        var signature = new byte[4];
        var read = await stream.ReadAsync(signature.AsMemory(), cancellationToken);
        if (read != 4 || signature[0] != 0x50 || signature[1] != 0x4B)
        {
            return false;
        }

        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return archive.GetEntry("[Content_Types].xml") is not null && archive.GetEntry(requiredEntry) is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool HasDisallowedTextControl(ReadOnlySpan<byte> content)
    {
        foreach (var value in content)
        {
            if (value < 0x20 && value is not (0x09 or 0x0A or 0x0D or 0x0C))
            {
                return true;
            }
        }
        return false;
    }
}
