using Microsoft.Data.Sqlite;

namespace Foundry.WebApi.Persistence;

/// <summary>
/// SQLite creates a missing database file but not a missing parent directory, so a fresh clone (where the
/// git-ignored <c>data/</c> folder does not exist yet) fails with "unable to open database file". This creates
/// the directory the connection string's data source lives in before the database is first opened.
/// </summary>
internal static class SqliteDataDirectory
{
    public static void EnsureExists(string connectionString)
    {
        SqliteConnectionStringBuilder builder = new(connectionString);
        string dataSource = builder.DataSource;

        if (builder.Mode == SqliteOpenMode.Memory
            || string.IsNullOrWhiteSpace(dataSource)
            || dataSource == ":memory:"
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
