using System.Text;
using Microsoft.AspNetCore.Hosting;
using ContosoDashboard.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContosoDashboard.Tests.DocumentManagement;

public class DocumentStorageTests
{
    [Fact]
    public async Task StageAsync_UsesGeneratedContainedPathsAndFinalizesFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ContosoDashboard.StorageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = new TestWebHostEnvironment(root);
            var options = Options.Create(new DocumentStorageOptions { RootPath = "private/uploads" });
            var storage = new LocalFileStorageService(options, environment, NullLogger<LocalFileStorageService>.Instance);
            await using var input = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nfixture"));

            var staged = await storage.StageAsync(input, 4, null, ".pdf");
            Assert.StartsWith("_staging/4/", staged.StagingRelativePath, StringComparison.Ordinal);
            Assert.StartsWith("4/personal/", staged.FinalRelativePath, StringComparison.Ordinal);
            Assert.StartsWith(
                Path.Combine(root, "private", "uploads"),
                Path.GetFullPath(storage.GetAbsolutePath(staged.FinalRelativePath)),
                StringComparison.Ordinal);
            var finalPath = await storage.FinalizeAsync(staged);
            Assert.Equal(staged.FinalRelativePath, finalPath);
            Assert.True(await storage.ExistsAsync(finalPath));
            await storage.DeleteAsync(finalPath);
            Assert.False(await storage.ExistsAsync(finalPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void GetAbsolutePath_RejectsPathTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "ContosoDashboard.StorageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var storage = new LocalFileStorageService(
                Options.Create(new DocumentStorageOptions()),
                new TestWebHostEnvironment(root),
                NullLogger<LocalFileStorageService>.Instance);
            Assert.Throws<InvalidOperationException>(() => storage.GetAbsolutePath("../outside.txt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

internal sealed class TestWebHostEnvironment(string contentRoot) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "ContosoDashboard.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = contentRoot;
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = contentRoot;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
