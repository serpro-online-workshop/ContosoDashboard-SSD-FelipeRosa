using System.Security.Claims;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public interface IDocumentService
{
    Task<DocumentOperationResult> UploadAsync(
        ClaimsPrincipal actor,
        DocumentUploadRequest request,
        Stream content,
        string originalFileName,
        string reportedContentType,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    Task<DocumentPageResult> SearchAsync(
        ClaimsPrincipal actor,
        DocumentSearchOptions options,
        CancellationToken cancellationToken = default);

    Task<Document?> GetAccessibleDocumentAsync(
        ClaimsPrincipal actor,
        int documentId,
        bool preview = false,
        CancellationToken cancellationToken = default);

    Task RecordDownloadAsync(ClaimsPrincipal actor, Document document, CancellationToken cancellationToken = default);

    Task<bool> UpdateMetadataAsync(
        ClaimsPrincipal actor,
        int documentId,
        string title,
        string? description,
        string category,
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default);

    Task<DocumentOperationResult> ReplaceFileAsync(
        ClaimsPrincipal actor,
        int documentId,
        Stream content,
        string originalFileName,
        string reportedContentType,
        bool allowUnscanned = false,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(ClaimsPrincipal actor, int documentId, CancellationToken cancellationToken = default);

    Task<bool> ShareAsync(
        ClaimsPrincipal actor,
        int documentId,
        DocumentShareRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeShareAsync(
        ClaimsPrincipal actor,
        int documentId,
        int shareId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentListItem>> GetSharedWithMeAsync(
        ClaimsPrincipal actor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentShare>> GetDocumentSharesAsync(
        ClaimsPrincipal actor,
        int documentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentListItem>> GetProjectDocumentsAsync(
        ClaimsPrincipal actor,
        int projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentListItem>> GetTaskDocumentsAsync(
        ClaimsPrincipal actor,
        int taskId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentListItem>> GetAttachableDocumentsAsync(
        ClaimsPrincipal actor,
        int taskId,
        CancellationToken cancellationToken = default);

    Task<bool> AttachToTaskAsync(
        ClaimsPrincipal actor,
        int documentId,
        int taskId,
        CancellationToken cancellationToken = default);

    Task<DocumentReportResult?> GetReportsAsync(
        ClaimsPrincipal actor,
        CancellationToken cancellationToken = default);
}
