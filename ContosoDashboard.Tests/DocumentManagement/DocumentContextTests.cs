using System.Text;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContosoDashboard.Tests.DocumentManagement;

public class DocumentContextTests
{
    [Fact]
    public async Task UploadAsync_TaskDocumentInheritsItsProjectAndNotifiesProjectMembers()
    {
        await using var environment = new DocumentTestEnvironment();
        await using var content = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\ntask document"));
        var request = new DocumentUploadRequest("Task notes", null, "Project Documents", null, 2, []);

        var result = await environment.Service.UploadAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee),
            request,
            content,
            "task-notes.pdf",
            "application/pdf");

        Assert.True(result.Succeeded);
        var saved = await environment.Context.Documents.SingleAsync();
        Assert.Equal(2, saved.TaskId);
        Assert.Equal(1, saved.ProjectId);
        var notifications = await environment.Context.Notifications.ToListAsync();
        Assert.Contains(notifications, notification => notification.UserId == 2 && notification.Type == NotificationType.DocumentAddedToProject);
        Assert.Contains(notifications, notification => notification.UserId == 3 && notification.Type == NotificationType.DocumentAddedToProject);
        Assert.DoesNotContain(notifications, notification => notification.UserId == 4);
    }

    [Fact]
    public async Task AttachToTaskAsync_RequiresTaskAccessAndInheritsProject()
    {
        await using var environment = new DocumentTestEnvironment();
        var document = await AddDocumentAsync(environment, 4, "Personal notes");
        var employee = DocumentTestEnvironment.Principal(4, UserRole.Employee);

        var attached = await environment.Service.AttachToTaskAsync(employee, document.DocumentId, 2);
        var denied = await environment.Service.AttachToTaskAsync(
            DocumentTestEnvironment.Principal(1, UserRole.Employee), document.DocumentId, 2);

        Assert.True(attached);
        Assert.False(denied);
        var saved = await environment.Context.Documents.SingleAsync();
        Assert.Equal(2, saved.TaskId);
        Assert.Equal(1, saved.ProjectId);
        Assert.Contains(await environment.Context.DocumentActivities.ToListAsync(), activity => activity.ActivityType == DocumentActivityType.TaskAttached);
    }

    [Fact]
    public async Task AttachToTaskAsync_RejectsProjectMismatch()
    {
        await using var environment = new DocumentTestEnvironment();
        var document = await AddDocumentAsync(environment, 4, "Existing project document", projectId: 1);
        var task = new TaskItem
        {
            Title = "Personal task",
            AssignedUserId = 4,
            CreatedByUserId = 2,
            ProjectId = null,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        environment.Context.Tasks.Add(task);
        await environment.Context.SaveChangesAsync();

        var attached = await environment.Service.AttachToTaskAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee), document.DocumentId, task.TaskId);

        Assert.False(attached);
        Assert.Null((await environment.Context.Documents.SingleAsync()).TaskId);
    }

    [Fact]
    public async Task DashboardSummary_ContainsOnlyUsersOwnCountAndFiveRecentDocuments()
    {
        await using var environment = new DocumentTestEnvironment();
        for (var index = 0; index < 6; index++)
        {
            environment.Context.Documents.Add(NewDocument(4, $"Employee {index}", DateTime.UtcNow.AddMinutes(index)));
        }
        environment.Context.Documents.Add(NewDocument(2, "Other user's file", DateTime.UtcNow.AddHours(1)));
        await environment.Context.SaveChangesAsync();

        var summary = await new DashboardService(environment.Context).GetDashboardSummaryAsync(4);

        Assert.Equal(6, summary.DocumentCount);
        Assert.Equal(5, summary.RecentDocuments.Count);
        Assert.DoesNotContain(summary.RecentDocuments, document => document.Title == "Other user's file");
        Assert.Equal("Employee 5", summary.RecentDocuments[0].Title);
    }

    private static async Task<Document> AddDocumentAsync(
        DocumentTestEnvironment environment,
        int ownerId,
        string title,
        int? projectId = null)
    {
        var document = NewDocument(ownerId, title, DateTime.UtcNow, projectId);
        document.StoragePath = $"{ownerId}/personal/{Guid.NewGuid():N}.pdf";
        environment.Context.Documents.Add(document);
        await environment.Context.SaveChangesAsync();
        return document;
    }

    private static Document NewDocument(int ownerId, string title, DateTime uploadedAt, int? projectId = null) => new()
    {
        Title = title,
        Category = "Project Documents",
        OriginalFileName = $"{title}.pdf",
        StoragePath = $"{ownerId}/personal/{Guid.NewGuid():N}.pdf",
        FileType = "application/pdf",
        FileSizeBytes = 64,
        UploadedAtUtc = uploadedAt,
        UploadedByUserId = ownerId,
        ProjectId = projectId,
        MalwareScanOutcome = MalwareScanOutcome.Clean
    };
}
