using Foundry.WebApi.Shared.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Persistence.FoundryDbContextTests;

public sealed class Instantiation
{
    [Fact]
    public void GivenOptions_CanInstantiateDbContext()
    {
        // Arrange
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseInMemoryDatabase("test")
            .Options;

        // Act
        using FoundryDbContext context = new(options);

        // Assert
        context.ShouldNotBeNull();
    }
}
