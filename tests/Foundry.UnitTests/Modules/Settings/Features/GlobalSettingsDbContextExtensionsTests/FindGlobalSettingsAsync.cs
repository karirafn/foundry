using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsDbContextExtensionsTests;

public sealed class FindGlobalSettingsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public FindGlobalSettingsAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private async Task SeedAsync()
    {
        await using FoundryDbContext seedDb = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdateLimits(7, 90);
        seedDb.Set<GlobalSettings>().Add(settings);
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenRowExists_ReturnsItTrackedByItsKey()
    {
        // Arrange
        await SeedAsync();
        await using FoundryDbContext dbContext = CreateDbContext();

        // Act
        GlobalSettings? result = await dbContext.FindGlobalSettingsAsync(TestContext.Current.CancellationToken);

        // Assert
        GlobalSettings settings = result.ShouldNotBeNull();
        settings.ShouldSatisfyAllConditions(
            () => settings.Id.ShouldBe(GlobalSettingsId.Default),
            () => settings.MaxConcurrent.ShouldBe(7),
            () => dbContext.Entry(settings).State.ShouldBe(EntityState.Unchanged));
    }

    [Fact]
    public async Task WhenNoRowExists_ReturnsNull()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();

        // Act
        GlobalSettings? result = await dbContext.FindGlobalSettingsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenOnlyRowHasAnotherKey_ReturnsNull()
    {
        // Arrange
        await SeedAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE global_settings SET id = '11111111-1111-1111-1111-111111111111'",
            TestContext.Current.CancellationToken);

        // Act
        GlobalSettings? result = await dbContext.FindGlobalSettingsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }
}
