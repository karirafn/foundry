using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features.AwaitingInput;
using Foundry.Shared;
using Foundry.WebApi.Persistence;
using Foundry.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

using Foundry.UnitTests.Modules.Settings.Features;

namespace Foundry.UnitTests.Modules.Settings.Features.AwaitingInput.UpdateAwaitingInputSettingsTests;

public sealed class HandleAsync : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();

        await using FoundryDbContext setup = CreateDbContext();
        await setup.Database.EnsureCreatedAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private async Task SeedSettingsAsync()
    {
        await using FoundryDbContext seedDb = CreateDbContext();
        seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> ReadStoredDelayAsync()
    {
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettings stored = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        return stored.AwaitingInputReminderDelayHours;
    }

    [Fact]
    public async Task WhenTheDelayIsPositive_PersistsItAndReturnsItOnTheSummary()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAwaitingInputSettings.Handler sut = new(dbContext, new StubWebhookIngressProbe(), new NullDispatchPauseQueries());

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new UpdateAwaitingInputSettings.Command(ReminderDelayHours: 12),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>().Value.AwaitingInputReminderDelayHours.ShouldBe(12);
        (await ReadStoredDelayAsync()).ShouldBe(12);
    }

    [Fact]
    public async Task WhenTheDelayIsZero_FailsAndKeepsTheStoredDelay()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAwaitingInputSettings.Handler sut = new(dbContext, new StubWebhookIngressProbe(), new NullDispatchPauseQueries());

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new UpdateAwaitingInputSettings.Command(ReminderDelayHours: 0),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>().Error.Code
            .ShouldBe("Settings.InvalidAwaitingInputReminderDelay");
        (await ReadStoredDelayAsync()).ShouldBe(72);
    }

    [Fact]
    public async Task WhenNoSettingsExist_FailsWithNotFound()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateAwaitingInputSettings.Handler sut = new(dbContext, new StubWebhookIngressProbe(), new NullDispatchPauseQueries());

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new UpdateAwaitingInputSettings.Command(ReminderDelayHours: 12),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>().Error.Code.ShouldBe("Settings.NotFound");
    }
}
