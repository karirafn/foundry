using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Foundry.IntegrationTests;

/// <summary>
/// One in-memory database migrated once per test process. A test host copies it instead of migrating its own
/// database, because every migration run logs the same EF Core warnings once per host.
/// </summary>
internal static class MigratedDatabaseTemplate
{
    // An in-memory SQLite database lives only as long as a connection to it, so this one stays open for the process.
    private static readonly Lazy<SqliteConnection> Template = new(Migrate, LazyThreadSafetyMode.ExecutionAndPublication);

    // A SqliteConnection is not thread-safe, and test classes construct their factories in parallel.
    private static readonly Lock CopyLock = new();

    public static void CopyInto(SqliteConnection destination)
    {
        SqliteConnection template = Template.Value;
        lock (CopyLock)
        {
            template.BackupDatabase(destination);
        }
    }

    private static SqliteConnection Migrate()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(connection)
            .Options;
        using FoundryDbContext dbContext = new(options, DataProtectionProvider.Create("migrated-database-template"));
        dbContext.Database.Migrate();

        return connection;
    }
}
