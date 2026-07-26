using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Persistence.FoundryDbContextTests;

public sealed class OutboxEntitiesRegistered
{
    [Fact]
    public void WhenInstantiated_ModelContainsOutboxMessage()
    {
        // Arrange
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(connection)
            .Options;

        // Act
        using FoundryDbContext context = new(options);

        // Assert
        context.Model.FindEntityType(typeof(OutboxMessage)).ShouldNotBeNull();
    }

    [Fact]
    public void WhenInstantiated_ModelContainsProcessedEvent()
    {
        // Arrange
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(connection)
            .Options;

        // Act
        using FoundryDbContext context = new(options);

        // Assert
        context.Model.FindEntityType(typeof(ProcessedEvent)).ShouldNotBeNull();
    }
}
