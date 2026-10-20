using System.Data.Common;
using ContosoDashboard.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContosoDashboard.Tests.DocumentManagement;

public class DatabaseMigrationInitializerTests
{
    [Fact]
    public async Task MigrateAsync_CreatesFreshSchema()
    {
        var databasePath = NewDatabasePath();
        try
        {
            await using var context = CreateContext(databasePath);
            await DatabaseMigrationInitializer.MigrateAsync(context, NullLogger.Instance);

            Assert.True(await context.Database.CanConnectAsync());
            Assert.Equal(0, await context.Documents.CountAsync());
            var applied = await context.Database.GetAppliedMigrationsAsync();
            Assert.Contains(DatabaseMigrationInitializer.InitialMigrationId, applied);
            Assert.Contains("20261020193356_DocumentSchemaCompatibility", applied);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task MigrateAsync_BaselinesLegacyEnsureCreatedSchemaAndPreservesRows()
    {
        var databasePath = NewDatabasePath();
        try
        {
            await CreateLegacySchemaAsync(databasePath);
            await using var context = CreateContext(databasePath);
            await DatabaseMigrationInitializer.MigrateAsync(context, NullLogger.Instance);

            Assert.Empty(await context.Documents.ToListAsync());
            var connection = context.Database.GetDbConnection();
            await context.Database.OpenConnectionAsync();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Users WHERE UserId = 4 AND Email = 'ni.kang@contoso.com';";
                Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }

            var applied = await context.Database.GetAppliedMigrationsAsync();
            Assert.Contains(DatabaseMigrationInitializer.InitialMigrationId, applied);
            Assert.Contains("20261020193356_DocumentSchemaCompatibility", applied);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static ApplicationDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task CreateLegacySchemaAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Users (UserId INTEGER PRIMARY KEY, Email TEXT NOT NULL, DisplayName TEXT NOT NULL, Department TEXT NULL, Role INTEGER NOT NULL);
            CREATE TABLE Projects (ProjectId INTEGER PRIMARY KEY, Name TEXT NOT NULL, ProjectManagerId INTEGER NOT NULL);
            CREATE TABLE Tasks (TaskId INTEGER PRIMARY KEY, AssignedUserId INTEGER NOT NULL, CreatedByUserId INTEGER NOT NULL, ProjectId INTEGER NULL);
            CREATE TABLE TaskComments (CommentId INTEGER PRIMARY KEY, TaskId INTEGER NOT NULL, UserId INTEGER NOT NULL);
            CREATE TABLE Notifications (NotificationId INTEGER PRIMARY KEY, UserId INTEGER NOT NULL);
            CREATE TABLE ProjectMembers (ProjectMemberId INTEGER PRIMARY KEY, ProjectId INTEGER NOT NULL, UserId INTEGER NOT NULL);
            CREATE TABLE Announcements (AnnouncementId INTEGER PRIMARY KEY, CreatedByUserId INTEGER NOT NULL);
            INSERT INTO Users (UserId, Email, DisplayName, Department, Role)
            VALUES (4, 'ni.kang@contoso.com', 'Ni Kang', 'Engineering', 0);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static string NewDatabasePath()
        => Path.Combine(Path.GetTempPath(), $"ContosoDashboard.MigrationTests.{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var file = path + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
