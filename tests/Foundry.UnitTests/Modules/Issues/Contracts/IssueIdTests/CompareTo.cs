using Foundry.Modules.Issues.Contracts;

using Microsoft.Data.Sqlite;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.IssueIdTests;

/// <summary>
/// Tests for <see cref="IssueId.CompareTo"/> and the corresponding comparison operators.
/// The implementation uses ordinal "D"-string comparison to match SQLite's TEXT column
/// ordering — see <see cref="WhenOrderedByCompareTo_MatchesSQLiteTextColumnOrder"/> for
/// the test that verifies this contractual guarantee.
/// </summary>
public sealed class CompareTo : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public CompareTo()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using SqliteCommand create = _connection.CreateCommand();
        create.CommandText = "CREATE TABLE issue_ids (id TEXT NOT NULL PRIMARY KEY)";
        create.ExecuteNonQuery();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public void WhenLeftStringOrdersPriorToRight_ReturnNegative()
    {
        // Arrange — ordinal string ordering: "00000000-..." < "ff000000-..."
        // IssueId.CompareTo uses string.CompareOrdinal("D") to match SQLite TEXT column order.
        IssueId lower = IssueId.From(new Guid("00000000-0000-0000-0000-000000000001"));
        IssueId higher = IssueId.From(new Guid("ff000000-0000-0000-0000-000000000001"));

        // Act
        int result = lower.CompareTo(higher);

        // Assert
        result.ShouldBeLessThan(0);
    }

    [Fact]
    public void WhenLeftStringOrdersAfterRight_ReturnPositive()
    {
        // Arrange — ordinal string ordering: "ff000000-..." > "00000000-..."
        // IssueId.CompareTo uses string.CompareOrdinal("D") to match SQLite TEXT column order.
        IssueId lower = IssueId.From(new Guid("00000000-0000-0000-0000-000000000001"));
        IssueId higher = IssueId.From(new Guid("ff000000-0000-0000-0000-000000000001"));

        // Act
        int result = higher.CompareTo(lower);

        // Assert
        result.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void WhenEqual_ReturnZero()
    {
        // Arrange
        Guid guid = new("aabbccdd-1122-3344-5566-778899aabbcc");
        IssueId left = IssueId.From(guid);
        IssueId right = IssueId.From(guid);

        // Act
        int result = left.CompareTo(right);

        // Assert
        result.ShouldBe(0);
    }

    /// <summary>
    /// Verifies that sorting IssueIds with <see cref="IssueId.CompareTo"/> produces the same
    /// sequence as SQLite's <c>ORDER BY id</c> on a TEXT column.
    ///
    /// SQLite stores IssueId as TEXT and orders rows using ordinal byte-for-byte text
    /// comparison. IssueId.CompareTo uses string.CompareOrdinal on the "D" representation
    /// so in-memory ordering is guaranteed to agree with the database's ORDER BY and keyset
    /// WHERE clause (i.Id > cursorId), preventing pagination gaps or duplicates.
    ///
    /// EF Core's SQLite provider stores Guid values as uppercase TEXT
    /// (e.g. "AABBCCDD-EEFF-0011-2233-445566778899"). This test inserts uppercase values
    /// directly — as the database actually stores them — to verify that IssueId.CompareTo
    /// on the corresponding lowercase "D" strings produces the identical sequence.
    /// </summary>
    [Fact]
    public async Task WhenOrderedByCompareTo_MatchesSQLiteTextColumnOrder()
    {
        // Arrange — IssueIds chosen to cover digit boundaries and hex-letter ranges across
        // all fields of the "D" format (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).
        // Inserted into SQLite as UPPERCASE TEXT, mirroring how EF Core stores Guid columns.
        IssueId id1 = IssueId.From(new Guid("00000001-0000-0000-0000-000000000000"));
        IssueId id2 = IssueId.From(new Guid("0000000a-0000-0000-0000-000000000000"));
        IssueId id3 = IssueId.From(new Guid("0000000f-0000-0000-0000-000000000000"));
        IssueId id4 = IssueId.From(new Guid("00000010-0000-0000-0000-000000000000"));
        IssueId id5 = IssueId.From(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"));

        IssueId[] allIds = [id1, id2, id3, id4, id5];
        foreach (IssueId id in allIds)
        {
            // EF Core SQLite stores Guid as uppercase "D" text; insert in the same format.
            using SqliteCommand insert = _connection.CreateCommand();
            insert.CommandText = "INSERT INTO issue_ids (id) VALUES (@id)";
            insert.Parameters.AddWithValue("@id", id.Value.ToString("D").ToUpperInvariant());
            await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // Act — read ids back in the order SQLite produces via ORDER BY on the TEXT column
        List<string> sqliteOrder = [];
        using SqliteCommand select = _connection.CreateCommand();
        select.CommandText = "SELECT id FROM issue_ids ORDER BY id";
        await using SqliteDataReader reader = await select.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            sqliteOrder.Add(reader.GetString(0));
        }

        // Assert — sorting the same ids with IssueId.CompareTo must produce the same sequence
        // as SQLite's ORDER BY; a mismatch means the in-memory keyset ordering diverges
        // from what the database uses for its WHERE (i.Id > cursorId) clause.
        List<IssueId> inMemorySorted = [..allIds];
        inMemorySorted.Sort();

        List<string> inMemoryOrder = inMemorySorted
            .Select(id => id.Value.ToString("D").ToUpperInvariant())
            .ToList();

        inMemoryOrder.ShouldBe(sqliteOrder);
    }
}
