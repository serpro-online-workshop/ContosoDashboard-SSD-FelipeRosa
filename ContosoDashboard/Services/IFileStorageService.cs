namespace ContosoDashboard.Services;

public interface IFileStorageService
{
    Task<StagedDocumentFile> StageAsync(
        Stream source,
        int userId,
        int? projectId,
        string extension,
        CancellationToken cancellationToken = default,
        IProgress<long>? progress = null);

    Task<string> FinalizeAsync(StagedDocumentFile stagedFile, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default);
    string GetAbsolutePath(string relativePath);
}

public sealed record StagedDocumentFile(string StagingRelativePath, string FinalRelativePath, long SizeBytes);
