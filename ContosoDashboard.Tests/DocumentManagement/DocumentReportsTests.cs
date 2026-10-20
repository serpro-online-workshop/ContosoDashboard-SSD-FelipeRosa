using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContosoDashboard.Tests.DocumentManagement;

public class DocumentReportsTests
{
    [Fact]
    public async Task GetReportsAsync_ReturnsUsageAndAccessPatternsOnlyForAdministrators()
    {
        await using var environment = new DocumentTestEnvironment();
        var document = new Document
        {
            Title = "Quarterly report",
            Category = "Reports",
            OriginalFileName = "quarterly.pdf",
            StoragePath = "4/personal/quarterly.pdf",
            FileType = "application/pdf",
            FileSizeBytes = 512,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = 4,
            MalwareScanOutcome = MalwareScanOutcome.Clean
        };
        environment.Context.Documents.Add(document);
        await environment.Context.SaveChangesAsync();
        environment.Context.DocumentActivities.AddRange(
            new DocumentActivity
            {
                DocumentId = document.DocumentId,
                DocumentTitleSnapshot = document.Title,
                ActivityType = DocumentActivityType.Uploaded,
                ActorUserId = 4
            },
            new DocumentActivity
            {
                DocumentId = document.DocumentId,
                DocumentTitleSnapshot = document.Title,
                ActivityType = DocumentActivityType.Downloaded,
                ActorUserId = 4
            });
        await environment.Context.SaveChangesAsync();

        var adminReport = await environment.Service.GetReportsAsync(
            DocumentTestEnvironment.Principal(1, UserRole.Administrator));
        var deniedReport = await environment.Service.GetReportsAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee));

        Assert.NotNull(adminReport);
        Assert.Contains(adminReport.FileTypes, item => item.FileType == "application/pdf" && item.Count == 1);
        Assert.Contains(adminReport.Uploaders, item => item.UserId == 4 && item.Count == 1);
        Assert.Contains(adminReport.AccessPatterns, item => item.ActivityType == DocumentActivityType.Downloaded && item.Count == 1);
        Assert.Null(deniedReport);
    }
}
