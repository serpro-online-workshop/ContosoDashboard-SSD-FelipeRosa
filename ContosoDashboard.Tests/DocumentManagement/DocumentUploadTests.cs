using System.Text;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ContosoDashboard.Tests.DocumentManagement;

public class DocumentUploadTests
{
    [Fact]
    public async Task UploadAsync_CleanPdfPersistsMetadataAndActivity()
    {
        await using var environment = new DocumentTestEnvironment();
        var result = await UploadAsync(environment, "%PDF-1.7\nvalid test pdf");

        Assert.True(result.Succeeded);
        Assert.False(result.ScanWarning);
        var document = await environment.Context.Documents.Include(item => item.Tags).SingleAsync();
        Assert.Equal(4, document.UploadedByUserId);
        Assert.Equal("Project Documents", document.Category);
        Assert.Equal("application/pdf", document.FileType);
        Assert.Equal(MalwareScanOutcome.Clean, document.MalwareScanOutcome);
        Assert.Equal(["planning", "roadmap"], document.Tags.Select(tag => tag.Tag).Order().ToArray());
        Assert.Equal(DocumentActivityType.Uploaded, (await environment.Context.DocumentActivities.SingleAsync()).ActivityType);
        Assert.True(await environment.Storage.ExistsAsync(document.StoragePath));
    }

    [Fact]
    public async Task UploadAsync_InfectedFileIsRejectedAndStagingIsRemoved()
    {
        await using var environment = new DocumentTestEnvironment();
        environment.Scanner.Result = MalwareScanResult.Infected;

        var result = await UploadAsync(environment, "%PDF-1.7\ninfected fixture");

        Assert.False(result.Succeeded);
        Assert.Equal("infected-file-rejected", result.ErrorCode);
        Assert.Empty(await environment.Context.Documents.ToListAsync());
        Assert.Empty(await environment.Context.DocumentActivities.ToListAsync());
        Assert.Empty(Directory.Exists(environment.Storage.GetAbsolutePath("_staging"))
            ? Directory.GetFiles(environment.Storage.GetAbsolutePath("_staging"), "*", SearchOption.AllDirectories)
            : []);
    }

    [Theory]
    [InlineData(MalwareScanResult.Unavailable, MalwareScanOutcome.Unavailable)]
    [InlineData(MalwareScanResult.Inconclusive, MalwareScanOutcome.Inconclusive)]
    public async Task UploadAsync_UnavailableOrInconclusiveScanWarnsAndAllows(
        MalwareScanResult scannerResult,
        MalwareScanOutcome storedOutcome)
    {
        await using var environment = new DocumentTestEnvironment();
        environment.Scanner.Result = scannerResult;

        var result = await UploadAsync(environment, "%PDF-1.7\nfixture", allowUnscanned: true);

        Assert.True(result.Succeeded);
        Assert.True(result.ScanWarning);
        Assert.Equal(storedOutcome, (await environment.Context.Documents.SingleAsync()).MalwareScanOutcome);
    }

    [Fact]
    public async Task UploadAsync_UnavailableScanRequiresExplicitConfirmation()
    {
        await using var environment = new DocumentTestEnvironment();
        environment.Scanner.Result = MalwareScanResult.Unavailable;

        var result = await UploadAsync(environment, "%PDF-1.7\nfixture");

        Assert.False(result.Succeeded);
        Assert.True(result.ScanWarning);
        Assert.Equal("unscanned-confirmation-required", result.ErrorCode);
        Assert.Empty(await environment.Context.Documents.ToListAsync());
    }

    [Fact]
    public async Task UploadAsync_RejectsUnsupportedExtensionBeforeStaging()
    {
        await using var environment = new DocumentTestEnvironment();
        await using var content = new MemoryStream(Encoding.ASCII.GetBytes("not an executable"));
        var request = Request();

        var result = await environment.Service.UploadAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee), request, content, "payload.exe", "application/octet-stream");

        Assert.False(result.Succeeded);
        Assert.Equal("unsupported-file-type", result.ErrorCode);
        Assert.Empty(await environment.Context.Documents.ToListAsync());
    }

    [Fact]
    public async Task UploadAsync_RejectsPdfWithMismatchedContent()
    {
        await using var environment = new DocumentTestEnvironment();
        var result = await UploadAsync(environment, "this is not a pdf");

        Assert.False(result.Succeeded);
        Assert.Equal("file-content-invalid", result.ErrorCode);
        Assert.Empty(await environment.Context.Documents.ToListAsync());
    }

    [Fact]
    public async Task UploadAsync_RejectsMissingTitleOrCategory()
    {
        await using var environment = new DocumentTestEnvironment();
        await using var content = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nfixture"));
        var request = Request() with { Title = " ", Category = "Unknown" };

        var result = await environment.Service.UploadAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee), request, content, "file.pdf", "application/pdf");

        Assert.False(result.Succeeded);
        Assert.Equal("invalid-metadata", result.ErrorCode);
        Assert.Empty(await environment.Context.Documents.ToListAsync());
    }

    [Fact]
    public async Task UploadAsync_RejectsFilesLargerThan25Mb()
    {
        await using var environment = new DocumentTestEnvironment();
        await using var content = new MemoryStream(new byte[26_214_401]);
        content.Write(Encoding.ASCII.GetBytes("%PDF-1.7"));
        content.Position = 0;

        var result = await environment.Service.UploadAsync(
            DocumentTestEnvironment.Principal(4, UserRole.Employee), Request(), content, "large.pdf", "application/pdf");

        Assert.False(result.Succeeded);
        Assert.Equal("file-size-or-content-invalid", result.ErrorCode);
        Assert.Empty(await environment.Context.Documents.ToListAsync());
    }

    [Fact]
    public async Task UploadAsync_DatabaseFailureRemovesFinalFile()
    {
        await using var environment = new DocumentTestEnvironment();
        var result = await UploadAsync(environment, "%PDF-1.7\nfixture", userId: 999);

        Assert.False(result.Succeeded);
        Assert.Equal("upload-failed", result.ErrorCode);
        Assert.Empty(await environment.Context.Documents.ToListAsync());
        Assert.Empty(Directory.Exists(environment.Storage.GetAbsolutePath("_staging"))
            ? Directory.GetFiles(environment.Storage.GetAbsolutePath("_staging"), "*", SearchOption.AllDirectories)
            : []);
        Assert.Empty(Directory.Exists(environment.Storage.GetAbsolutePath("999"))
            ? Directory.GetFiles(environment.Storage.GetAbsolutePath("999"), "*", SearchOption.AllDirectories)
            : []);
    }

    private static Task<DocumentOperationResult> UploadAsync(
        DocumentTestEnvironment environment,
        string data,
        int userId = 4,
        bool allowUnscanned = false)
        => UploadBytesAsync(environment, Encoding.ASCII.GetBytes(data), userId, allowUnscanned);

    private static Task<DocumentOperationResult> UploadBytesAsync(
        DocumentTestEnvironment environment,
        byte[] data,
        int userId,
        bool allowUnscanned = false)
    {
        var content = new MemoryStream(data, writable: false);
        return environment.Service.UploadAsync(
            DocumentTestEnvironment.Principal(userId, UserRole.Employee), Request() with { AllowUnscanned = allowUnscanned }, content, "plan.pdf", "application/pdf");
    }

    private static DocumentUploadRequest Request() => new(
        "Implementation Plan",
        "Design notes",
        "Project Documents",
        null,
        null,
        ["Planning", "Roadmap"]);
}
