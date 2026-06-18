using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetDispatchPauseStateAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetDispatchPauseStateAsync()
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
    public async Task WhenSettingsExist_ReturnsStoredDispatchPauseState()
    {
        // Arrange
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(2);
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.PauseDispatch();
            settings.SetUsageLimitResetsAt(resetsAt);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        DispatchPauseState result = await sut.GetDispatchPauseStateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsDispatchPaused.ShouldBeTrue(),
            () => result.AutoResumeOnUsageReset.ShouldBeTrue(),
            () => result.UsageLimitResetsAt.ShouldNotBeNull());
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsDefaultDispatchPauseState()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        DispatchPauseState result = await sut.GetDispatchPauseStateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsDispatchPaused.ShouldBeFalse(),
            () => result.AutoResumeOnUsageReset.ShouldBeTrue(),
            () => result.UsageLimitResetsAt.ShouldBeNull());
    }
}
