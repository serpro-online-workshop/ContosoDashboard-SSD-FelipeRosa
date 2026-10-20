using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Data;

public static class DatabaseMigrationInitializer
{
    public const string InitialMigrationId = "20261020193324_InitialCreate";
    private const string ProductVersion = "10.0.0";

    private static readonly IReadOnlyDictionary<string, string[]> LegacyRequiredColumns = new Dictionary<string, string[]>
    {
        ["Users"] = ["UserId", "Email", "DisplayName", "Department", "Role"],
        ["Projects"] = ["ProjectId", "Name", "ProjectManagerId"],
        ["Tasks"] = ["TaskId", "AssignedUserId", "CreatedByUserId"],
        ["TaskComments"] = ["CommentId", "TaskId", "UserId"],
        ["Notifications"] = ["NotificationId", "UserId"],
        ["ProjectMembers"] = ["ProjectMemberId", "ProjectId", "UserId"],
        ["Announcements"] = ["AnnouncementId", "CreatedByUserId"]
    };

    public static async Task MigrateAsync(ApplicationDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (context.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
        {
            throw new InvalidOperationException("Document management migrations require the SQLite provider.");
        }

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = context.Database.GetDbConnection();
            if (!await TableExistsAsync(connection, "__EFMigrationsHistory", cancellationToken))
            {
                var matchingLegacyTables = 0;
                foreach (var table in LegacyRequiredColumns)
                {
                    if (await TableHasColumnsAsync(connection, table.Key, table.Value, cancellationToken))
                    {
                        matchingLegacyTables++;
                    }
                }

                if (matchingLegacyTables != 0 && matchingLegacyTables != LegacyRequiredColumns.Count)
                {
                    throw new InvalidOperationException(
                        "The existing SQLite database has a partial or unrecognized schema. Back it up and resolve its schema before applying document migrations; the database was not modified.");
                }

                if (matchingLegacyTables == LegacyRequiredColumns.Count)
                {
                    await using var baseline = connection.CreateCommand();
                    baseline.CommandText = """
                        CREATE TABLE "__EFMigrationsHistory" (
                            "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                            "ProductVersion" TEXT NOT NULL
                        );
                        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                        VALUES ($migrationId, $productVersion);
                        """;
                    var migrationParameter = baseline.CreateParameter();
                    migrationParameter.ParameterName = "$migrationId";
                    migrationParameter.Value = InitialMigrationId;
                    baseline.Parameters.Add(migrationParameter);
                    var versionParameter = baseline.CreateParameter();
                    versionParameter.ParameterName = "$productVersion";
                    versionParameter.Value = ProductVersion;
                    baseline.Parameters.Add(versionParameter);
                    await baseline.ExecuteNonQueryAsync(cancellationToken);
                    logger.LogInformation("Recognized the existing training schema and preserved it as the EF Core migration baseline.");
                }
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        await context.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<bool> TableHasColumnsAsync(
        DbConnection connection,
        string tableName,
        IReadOnlyCollection<string> requiredColumns,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, tableName, cancellationToken))
        {
            return false;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(reader.GetString(1));
        }

        return requiredColumns.All(found.Contains);
    }
}
