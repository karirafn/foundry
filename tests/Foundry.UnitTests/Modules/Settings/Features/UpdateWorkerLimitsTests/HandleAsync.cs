using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.UpdateWorkerLimitsTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public HandleAsync()
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
    public async Task WhenSettingsExist_UpdatesLimitsAndReturnsSummary()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateWorkerLimits.Handler sut = new(dbContext);
        UpdateWorkerLimits.Command command = new(5, 60);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Success success =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.MaxConcurrent.ShouldBe(5),
            () => success.Value.TimeoutMinutes.ShouldBe(60));
    }

    [Fact]
    public async Task WhenSettingsExist_PersistsChangesToDatabase()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateWorkerLimits.Handler sut = new(dbContext);
            UpdateWorkerLimits.Command command = new(8, 240);
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.ShouldSatisfyAllConditions(
            () => stored.MaxConcurrent.ShouldBe(8),
            () => stored.TimeoutMinutes.ShouldBe(240));
    }

    [Fact]
    public async Task WhenSettingsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateWorkerLimits.Handler sut = new(dbContext);
        UpdateWorkerLimits.Command command = new(5, 60);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Failure failure =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.NotFoundCode);
    }
}
