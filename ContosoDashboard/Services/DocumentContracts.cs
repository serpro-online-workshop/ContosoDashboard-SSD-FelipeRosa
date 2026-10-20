using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public sealed record DocumentUploadRequest(
    string Title,
    string? Description,
    string Category,
    int? ProjectId,
    int? TaskId,
    IReadOnlyCollection<string> Tags,
    bool AllowUnscanned = false);

public sealed record DocumentSearchOptions(
    string? SearchText = null,
    string? Category = null,
    int? ProjectId = null,
    DateOnly? UploadedFrom = null,
    DateOnly? UploadedThrough = null,
    DocumentSortField SortBy = DocumentSortField.UploadedAt,
    bool Descending = true,
    int Page = 1,
    int PageSize = 25,
    bool OwnedOnly = true);

public enum DocumentSortField
{
    Title,
    UploadedAt,
    Category,
    FileSize
}

public sealed record DocumentListItem(
    int DocumentId,
    string Title,
    string Category,
    DateTime UploadedAtUtc,
    long FileSizeBytes,
    string? ProjectName,
    string UploaderName,
    string FileType,
    MalwareScanOutcome MalwareScanOutcome);

public sealed record DocumentPageResult(
    IReadOnlyList<DocumentListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record DocumentOperationResult(
    bool Succeeded,
    string? ErrorCode = null,
    bool ScanWarning = false,
    int? DocumentId = null);

public sealed record DocumentShareRequest(int? RecipientUserId, string? RecipientDepartment);

public sealed record DocumentReportResult(
    IReadOnlyList<DocumentTypeReportItem> FileTypes,
    IReadOnlyList<DocumentUploaderReportItem> Uploaders,
    IReadOnlyList<DocumentAccessReportItem> AccessPatterns);

public sealed record DocumentTypeReportItem(string FileType, int Count);
public sealed record DocumentUploaderReportItem(int UserId, string DisplayName, int Count);
public sealed record DocumentAccessReportItem(DocumentActivityType ActivityType, int Count);
