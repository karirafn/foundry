using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;
using Foundry.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

using Foundry.UnitTests.Modules.Settings.Features;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetAwaitingInputReminderDelayAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetAwaitingInputReminderDelayAsync()
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
    public async Task WhenSettingsExist_ReturnsTheStoredDelay()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.UpdateAwaitingInputReminderDelay(5);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext, new StubWebhookIngressProbe(), new NullDispatchPauseQueries());

        // Act
        TimeSpan result = await sut.GetAwaitingInputReminderDelayAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(TimeSpan.FromHours(5));
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsThreeDays()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext, new StubWebhookIngressProbe(), new NullDispatchPauseQueries());

        // Act
        TimeSpan result = await sut.GetAwaitingInputReminderDelayAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(TimeSpan.FromDays(3));
    }
}
