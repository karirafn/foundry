using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Persistence.FoundryDbContextTests;

public sealed class Instantiation
{
    [Fact]
    public void WhenInstantiated_ModelContainsRegisteredEntities()
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
        context.Model.GetEntityTypes().ShouldNotBeEmpty();
    }
}
