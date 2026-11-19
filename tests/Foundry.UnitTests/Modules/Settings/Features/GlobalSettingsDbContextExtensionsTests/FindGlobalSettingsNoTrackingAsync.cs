using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsDbContextExtensionsTests;

public sealed class FindGlobalSettingsNoTrackingAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public FindGlobalSettingsNoTrackingAsync()
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

    [Fact]
    public async Task WhenRowExists_ReturnsItUntrackedByItsKey()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();

        // Act
        GlobalSettings? result = await dbContext.FindGlobalSettingsNoTrackingAsync(TestContext.Current.CancellationToken);

        // Assert
        GlobalSettings settings = result.ShouldNotBeNull();
        settings.ShouldSatisfyAllConditions(
            () => settings.Id.ShouldBe(GlobalSettingsId.Default),
            () => dbContext.Entry(settings).State.ShouldBe(EntityState.Detached));
    }

    [Fact]
    public async Task WhenNoRowExists_ReturnsNull()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();

        // Act
        GlobalSettings? result = await dbContext.FindGlobalSettingsNoTrackingAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }
}
