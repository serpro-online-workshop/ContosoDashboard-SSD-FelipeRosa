using System.Text;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContosoDashboard.Tests.DocumentManagement;

public class DocumentManagementTests
{
    [Fact]
    public async Task UpdateMetadataAsync_UpdatesOnlyAuthorizedDocumentAndTags()
    {
        await using var environment = new DocumentTestEnvironment();
        var document = await AddStoredDocumentAsync(environment, 4, "Initial title", tags: ["old"]);

        var updated = await environment.Service.UpdateMetadataAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee),
            document.DocumentId,
            "Updated title",
            "Updated description",
            "Reports",
            ["new", "team"]);
        var unauthorized = await environment.Service.UpdateMetadataAsync(
            DocumentTestEnvironment.Principal(2, UserRole.ProjectManager),
            document.DocumentId,
            "Unauthorized title",
            null,
            "Other",
            []);

        Assert.True(updated);
        Assert.False(unauthorized);
        var saved = await environment.Context.Documents.Include(item => item.Tags).SingleAsync();
        Assert.Equal("Updated title", saved.Title);
        Assert.Equal("Updated description", saved.Description);
        Assert.Equal("Reports", saved.Category);
        Assert.Equal(new[] { "new", "team" }, saved.Tags.Select(tag => tag.Tag).Order());
        Assert.Contains(await environment.Context.DocumentActivities.ToListAsync(), activity => activity.ActivityType == DocumentActivityType.MetadataUpdated);
    }

    [Fact]
    public async Task ShareAsync_GrantsAndRevokesDirectAccessAndNotifiesRecipient()
    {
        await using var environment = new DocumentTestEnvironment();
        var document = await AddStoredDocumentAsync(environment, 4, "Shared design");
        environment.Context.Users.Add(new User
        {
            UserId = 5,
            Email = "alex.tan@contoso.com",
            DisplayName = "Alex Tan",
            Department = "Sales",
            JobTitle = "Analyst",
            Role = UserRole.Employee,
            CreatedDate = DateTime.UtcNow
        });
        await environment.Context.SaveChangesAsync();
        var owner = DocumentTestEnvironment.Principal(4, UserRole.Employee);
        var recipient = DocumentTestEnvironment.Principal(5, UserRole.Employee);

        var shared = await environment.Service.ShareAsync(owner, document.DocumentId, new DocumentShareRequest(5, null));
        Assert.True(shared);
        Assert.NotNull(await environment.Service.GetAccessibleDocumentAsync(recipient, document.DocumentId));
        Assert.Contains(await environment.Context.Notifications.ToListAsync(), notification => notification.UserId == 5 && notification.Type == NotificationType.DocumentShared);

        var share = await environment.Context.DocumentShares.SingleAsync();
        var revoked = await environment.Service.RevokeShareAsync(owner, document.DocumentId, share.DocumentShareId);

        Assert.True(revoked);
        Assert.Null(await environment.Service.GetAccessibleDocumentAsync(recipient, document.DocumentId));
        Assert.Contains(await environment.Context.DocumentActivities.ToListAsync(), activity => activity.ActivityType == DocumentActivityType.ShareRevoked);
    }

    [Fact]
    public async Task ProjectManagerCanManageProjectDocumentButCannotShareAsNonOwner()
    {
        await using var environment = new DocumentTestEnvironment();
        var document = await AddStoredDocumentAsync(environment, 4, "Project file", projectId: 1);
        var manager = DocumentTestEnvironment.Principal(2, UserRole.ProjectManager);

        var updated = await environment.Service.UpdateMetadataAsync(
            manager, document.DocumentId, "Managed title", null, "Reports", []);
        var shared = await environment.Service.ShareAsync(manager, document.DocumentId, new DocumentShareRequest(3, null));

        Assert.True(updated);
        Assert.False(shared);
        Assert.Empty(await environment.Context.DocumentShares.ToListAsync());
    }

    [Fact]
    public async Task ReplaceFileAsync_ReplacesPrivateFileWithoutVersionHistory()
    {
        await using var environment = new DocumentTestEnvironment();
        var document = await AddStoredDocumentAsync(environment, 4, "Replace me");
        var oldPath = document.StoragePath;
        await using var replacement = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nreplacement"));

        var result = await environment.Service.ReplaceFileAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee),
            document.DocumentId,
            replacement,
            "replacement.pdf",
            "application/pdf");

        Assert.True(result.Succeeded);
        var saved = await environment.Context.Documents.SingleAsync();
        Assert.NotEqual(oldPath, saved.StoragePath);
        Assert.Equal("replacement.pdf", saved.OriginalFileName);
        Assert.False(await environment.Storage.ExistsAsync(oldPath));
        Assert.Equal(1, await environment.Context.Documents.CountAsync());
        Assert.Contains(await environment.Context.DocumentActivities.ToListAsync(), activity => activity.ActivityType == DocumentActivityType.Replaced);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileAndDocumentButPreservesActivity()
    {
        await using var environment = new DocumentTestEnvironment();
        var document = await AddStoredDocumentAsync(environment, 4, "Delete me", tags: ["temporary"]);
        var path = document.StoragePath;

        var deleted = await environment.Service.DeleteAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee), document.DocumentId);

        Assert.True(deleted);
        Assert.Empty(await environment.Context.Documents.ToListAsync());
        Assert.Empty(await environment.Context.DocumentTags.ToListAsync());
        Assert.False(await environment.Storage.ExistsAsync(path));
        var activity = await environment.Context.DocumentActivities.SingleAsync();
        Assert.Equal(DocumentActivityType.Deleted, activity.ActivityType);
        Assert.Equal("Delete me", activity.DocumentTitleSnapshot);
    }

    private static async Task<Document> AddStoredDocumentAsync(
        DocumentTestEnvironment environment,
        int ownerId,
        string title,
        int? projectId = null,
        IReadOnlyCollection<string>? tags = null)
    {
        await using var content = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nseed file"));
        var staged = await environment.Storage.StageAsync(content, ownerId, projectId, ".pdf");
        var path = await environment.Storage.FinalizeAsync(staged);
        var document = new Document
        {
            Title = title,
            Category = "Project Documents",
            OriginalFileName = "source.pdf",
            StoragePath = path,
            FileType = "application/pdf",
            FileSizeBytes = staged.SizeBytes,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = ownerId,
            ProjectId = projectId,
            MalwareScanOutcome = MalwareScanOutcome.Clean
        };
        foreach (var tag in tags ?? [])
        {
            document.Tags.Add(new DocumentTag { Tag = tag });
        }
        environment.Context.Documents.Add(document);
        await environment.Context.SaveChangesAsync();
        return document;
    }
}
