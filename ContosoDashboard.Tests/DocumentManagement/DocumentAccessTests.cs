using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContosoDashboard.Tests.DocumentManagement;

public class DocumentAccessTests
{
    [Fact]
    public async Task SearchAsync_ReturnsOnlyOwnedSharedAndProjectDocuments()
    {
        await using var environment = new DocumentTestEnvironment();
        var ownerProject = AddDocument(environment, "Project design", 4, projectId: 1);
        var managerProject = AddDocument(environment, "Manager roadmap", 2, projectId: 1);
        var sharedPersonal = AddDocument(environment, "Shared report", 2);
        AddDocument(environment, "Admin private", 1);
        await environment.Context.SaveChangesAsync();
        environment.Context.DocumentShares.Add(new DocumentShare
        {
            DocumentId = sharedPersonal.DocumentId,
            RecipientUserId = 4,
            GrantedByUserId = 2
        });
        await environment.Context.SaveChangesAsync();

        var result = await environment.Service.SearchAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee),
            new DocumentSearchOptions(OwnedOnly: false));

        Assert.Equal(new[] { ownerProject.DocumentId, managerProject.DocumentId, sharedPersonal.DocumentId }.Order(),
            result.Items.Select(item => item.DocumentId).Order());
    }

    [Fact]
    public async Task SearchAsync_ExcludesUnauthorizedMatchesFromSearchResults()
    {
        await using var environment = new DocumentTestEnvironment();
        AddDocument(environment, "Authorized plan", 4);
        AddDocument(environment, "Secret plan", 2);
        await environment.Context.SaveChangesAsync();

        var result = await environment.Service.SearchAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee),
            new DocumentSearchOptions(SearchText: "plan", OwnedOnly: false));

        Assert.Single(result.Items);
        Assert.Equal("Authorized plan", result.Items[0].Title);
    }

    [Fact]
    public async Task GetAccessibleDocumentAsync_RejectsGuessedIdsButAllowsDepartmentLeadAndAdmin()
    {
        await using var environment = new DocumentTestEnvironment();
        var engineeringDocument = AddDocument(environment, "Engineering file", 4);
        var privateAdminDocument = AddDocument(environment, "IT private", 1);
        await environment.Context.SaveChangesAsync();

        Assert.Null(await environment.Service.GetAccessibleDocumentAsync(
            DocumentTestEnvironment.Principal(1, UserRole.Employee), engineeringDocument.DocumentId));
        Assert.NotNull(await environment.Service.GetAccessibleDocumentAsync(
            DocumentTestEnvironment.Principal(3, UserRole.TeamLead), engineeringDocument.DocumentId));
        Assert.NotNull(await environment.Service.GetAccessibleDocumentAsync(
            DocumentTestEnvironment.Principal(1, UserRole.Administrator), engineeringDocument.DocumentId));
        Assert.NotNull(await environment.Service.GetAccessibleDocumentAsync(
            DocumentTestEnvironment.Principal(1, UserRole.Administrator), privateAdminDocument.DocumentId));
    }

    [Fact]
    public async Task GetProjectDocumentsAsync_AllowsMembersAndProjectManagerOnly()
    {
        await using var environment = new DocumentTestEnvironment();
        var document = AddDocument(environment, "Project file", 2, projectId: 1);
        await environment.Context.SaveChangesAsync();

        var memberDocuments = await environment.Service.GetProjectDocumentsAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee), 1);
        var outsiderDocuments = await environment.Service.GetProjectDocumentsAsync(
            DocumentTestEnvironment.Principal(1, UserRole.Employee), 1);
        var managerDocuments = await environment.Service.GetProjectDocumentsAsync(
            DocumentTestEnvironment.Principal(2, UserRole.ProjectManager), 1);

        Assert.Contains(memberDocuments, item => item.DocumentId == document.DocumentId);
        Assert.Empty(outsiderDocuments);
        Assert.Contains(managerDocuments, item => item.DocumentId == document.DocumentId);
    }

    private static Document AddDocument(
        DocumentTestEnvironment environment,
        string title,
        int ownerId,
        int? projectId = null)
    {
        var document = new Document
        {
            Title = title,
            Category = "Project Documents",
            OriginalFileName = $"{title}.pdf",
            StoragePath = $"{ownerId}/personal/{Guid.NewGuid():N}.pdf",
            FileType = "application/pdf",
            FileSizeBytes = 128,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = ownerId,
            ProjectId = projectId,
            MalwareScanOutcome = MalwareScanOutcome.Clean
        };
        environment.Context.Documents.Add(document);
        return document;
    }
}
