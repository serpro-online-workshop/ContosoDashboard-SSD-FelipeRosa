using System.Security.Claims;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.DocumentManagement;

internal sealed class DocumentTestEnvironment : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ApplicationDbContext Context { get; }
    public TestFileStorage Storage { get; } = new();
    public StaticMalwareScanner Scanner { get; } = new();
    public DocumentService Service { get; }

    public DocumentTestEnvironment()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        Context = new ApplicationDbContext(options);
        Context.Database.EnsureCreated();
        Service = new DocumentService(
            Context,
            Storage,
            Scanner,
            new NotificationService(Context),
            NullLogger<DocumentService>.Instance);
    }

    public static ClaimsPrincipal Principal(int userId, UserRole role)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, $"Test User {userId}"),
            new Claim(ClaimTypes.Role, role.ToString())
        ], "Test");
        return new ClaimsPrincipal(identity);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
        Storage.Dispose();
    }
}

internal sealed class StaticMalwareScanner : IMalwareScanner
{
    public MalwareScanResult Result { get; set; } = MalwareScanResult.Clean;

    public Task<MalwareScanResult> ScanAsync(string absoluteStagedPath, CancellationToken cancellationToken = default)
        => Task.FromResult(Result);
}

internal sealed class TestFileStorage : IFileStorageService, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ContosoDashboard.Tests", Guid.NewGuid().ToString("N"));

    public async Task<StagedDocumentFile> StageAsync(
        Stream source,
        int userId,
        int? projectId,
        string extension,
        CancellationToken cancellationToken = default,
        IProgress<long>? progress = null)
    {
        var scope = projectId?.ToString() ?? "personal";
        var staging = $"_staging/{userId}/{Guid.NewGuid():N}.upload";
        var final = $"{userId}/{scope}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = GetAbsolutePath(staging);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var destination = File.Create(path);
        await source.CopyToAsync(destination, cancellationToken);
        var length = destination.Length;
        progress?.Report(length);
        if (length > 26_214_400)
        {
            File.Delete(path);
            throw new InvalidDataException("File exceeds the test storage limit.");
        }
        if (length == 0)
        {
            File.Delete(path);
            throw new InvalidDataException("Empty files are not accepted.");
        }
        return new StagedDocumentFile(staging, final, length);
    }

    public Task<string> FinalizeAsync(StagedDocumentFile stagedFile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = GetAbsolutePath(stagedFile.StagingRelativePath);
        var destination = GetAbsolutePath(stagedFile.FinalRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination);
        return Task.FromResult(stagedFile.FinalRelativePath);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetAbsolutePath(relativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = File.OpenRead(GetAbsolutePath(relativePath));
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(GetAbsolutePath(relativePath)));

    public string GetAbsolutePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Storage path escaped the test root.");
        }
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
