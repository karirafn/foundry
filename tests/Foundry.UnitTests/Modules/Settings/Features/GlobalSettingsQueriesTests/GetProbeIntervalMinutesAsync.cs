using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetProbeIntervalMinutesAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetProbeIntervalMinutesAsync()
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
    public async Task WhenSettingsExist_ReturnsStoredProbeIntervalMinutes()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.UpdateProbeInterval(15);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        int result = await sut.GetProbeIntervalMinutesAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(15);
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsDefault()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        int result = await sut.GetProbeIntervalMinutesAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(GlobalSettings.DefaultProbeIntervalMinutes);
    }
}
