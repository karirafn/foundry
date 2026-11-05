using Foundry.Shared.Infrastructure.DataProtection;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DataProtection.EncryptedColumnTests;

public sealed class DiscoverIn
{
    [Fact]
    public void FoundryModel_ReturnsEachEncryptedColumnOnce()
    {
        // Arrange
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(connection)
            .Options;
        using FoundryDbContext context = new(options);

        // Act
        IReadOnlyList<EncryptedColumn> columns = EncryptedColumn.DiscoverIn(context.Model);

        // Assert
        columns
            .Select(column => (column.Table, column.Column, column.KeyColumn))
            .OrderBy(column => column.Table)
            .ThenBy(column => column.Column)
            .ShouldBe(
            [
                ("accounts", "branch_rules_token", "id"),
                ("accounts", "token", "id"),
                ("claude_account", "api_key", "id"),
                ("monitored_repositories", "webhook_secret", "id"),
            ]);
    }

    [Fact]
    public void EncryptedColumnOnCompositeKeyEntity_ThrowsNamingTable()
    {
        // Arrange
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        DbContextOptions<CompositeKeyContext> options = new DbContextOptionsBuilder<CompositeKeyContext>()
            .UseSqlite(connection)
            .Options;
        using CompositeKeyContext context = new(options);

        // Act
        Action act = () => EncryptedColumn.DiscoverIn(context.Model);

        // Assert
        act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("composite_rows");
    }

    private sealed class CompositeRow
    {
        public int First { get; init; }

        public int Second { get; init; }

        public string Secret { get; init; } = string.Empty;
    }

    private sealed class MarkedConverter()
        : ValueConverter<string, string>(value => value, value => value), IDataProtectedConverter
    {
        public string ProtectorPurpose => "Foundry.Test.Purpose";
    }

    private sealed class CompositeKeyContext(DbContextOptions<CompositeKeyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CompositeRow>(entity =>
            {
                entity.ToTable("composite_rows");
                entity.HasKey(row => new { row.First, row.Second });
                entity.Property(row => row.Secret).HasConversion(new MarkedConverter());
            });
        }
    }
}
