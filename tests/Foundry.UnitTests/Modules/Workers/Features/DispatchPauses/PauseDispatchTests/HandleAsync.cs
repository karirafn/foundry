using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Features.DispatchPauses;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

using Foundry.UnitTests.Modules.Settings.Features;

namespace Foundry.UnitTests.Modules.Workers.Features.DispatchPauses.PauseDispatchTests;

public sealed class HandleAsync : IAsyncLifetime
{
    private static GlobalSettingsQueries SettingsQueries(FoundryDbContext dbContext) =>
        new(dbContext, new StubWebhookIngressProbe(), new DispatchPauseQueries(dbContext));

    private readonly SqliteConnection _connection;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
    }

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

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    [Fact]
    public async Task WhenSettingsExist_PausesDispatchAndReturnsSummary()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
            DispatchPause pause = DispatchPause.Create();
            seedDb.Set<DispatchPause>().Add(pause);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        PauseDispatch.Handler sut = new(dbContext, SettingsQueries(dbContext));
        PauseDispatch.Command command = new();

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Success success =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        success.Value.IsDispatchPaused.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenSettingsExist_PersistsPauseToDatabase()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
            DispatchPause pause = DispatchPause.Create();
            seedDb.Set<DispatchPause>().Add(pause);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            PauseDispatch.Handler sut = new(dbContext, SettingsQueries(dbContext));
            PauseDispatch.Command command = new();
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        DispatchPause? stored = await assertDb.Set<DispatchPause>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.IsDispatchPaused.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenSettingsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        PauseDispatch.Handler sut = new(dbContext, SettingsQueries(dbContext));
        PauseDispatch.Command command = new();

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Failure failure =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>();
        failure.Error.Code.ShouldBe(DispatchPauseErrors.SettingsNotFoundCode);
    }
}
