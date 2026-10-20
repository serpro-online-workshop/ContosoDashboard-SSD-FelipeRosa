using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services;

public sealed class LocalFileStorageService : IFileStorageService
{
    private const long MaxFileSizeBytes = 26_214_400;
    private const int CopyBufferSize = 64 * 1024;
    private readonly string _rootPath;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(
        IOptions<DocumentStorageOptions> options,
        IWebHostEnvironment environment,
        ILogger<LocalFileStorageService> logger)
    {
        options.Value.Validate();
        _rootPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath));
        _logger = logger;
    }

    public async Task<StagedDocumentFile> StageAsync(
        Stream source,
        int userId,
        int? projectId,
        string extension,
        CancellationToken cancellationToken = default,
        IProgress<long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        var safeExtension = NormalizeExtension(extension);
        var scope = projectId.HasValue && projectId.Value > 0
            ? projectId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "personal";
        var generatedName = $"{Guid.NewGuid():N}.{safeExtension}";
        var stagingRelativePath = $"_staging/{userId}/{Guid.NewGuid():N}.upload";
        var finalRelativePath = $"{userId}/{scope}/{generatedName}";
        var stagingAbsolutePath = GetAbsolutePath(stagingRelativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(stagingAbsolutePath)!);
        long totalBytes = 0;
        try
        {
            await using var destination = new FileStream(
                stagingAbsolutePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[CopyBufferSize];
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > MaxFileSizeBytes)
                {
                    throw new InvalidDataException("The file exceeds the 25 MB limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                progress?.Report(totalBytes);
            }

            if (totalBytes == 0)
            {
                throw new InvalidDataException("Empty files are not supported.");
            }

            return new StagedDocumentFile(stagingRelativePath, finalRelativePath, totalBytes);
        }
        catch
        {
            await DeleteIfExistsAsync(stagingRelativePath, CancellationToken.None);
            throw;
        }
    }

    public Task<string> FinalizeAsync(StagedDocumentFile stagedFile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stagingPath = GetAbsolutePath(stagedFile.StagingRelativePath);
        var finalPath = GetAbsolutePath(stagedFile.FinalRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        File.Move(stagingPath, finalPath, overwrite: false);
        return Task.FromResult(stagedFile.FinalRelativePath);
    }

    public async Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await DeleteIfExistsAsync(relativePath, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to remove a private document file.");
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            GetAbsolutePath(relativePath),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetAbsolutePath(relativePath)));
    }

    public string GetAbsolutePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Document paths must be non-empty relative paths.");
        }

        var absolutePath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        var relativeToRoot = Path.GetRelativePath(_rootPath, absolutePath);
        if (relativeToRoot == "." || relativeToRoot == ".." ||
            relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativeToRoot.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Document path resolves outside the configured storage root.");
        }

        return absolutePath;
    }

    private async Task DeleteIfExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var absolutePath = GetAbsolutePath(relativePath);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        await Task.CompletedTask;
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = extension.Trim().TrimStart('.').ToLowerInvariant();
        if (normalized.Length is < 1 or > 10 || normalized.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("The file extension is invalid.", nameof(extension));
        }

        return normalized;
    }
}
