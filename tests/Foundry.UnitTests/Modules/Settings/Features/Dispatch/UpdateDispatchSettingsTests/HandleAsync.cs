using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features.Dispatch;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.Dispatch.UpdateDispatchSettingsTests;

public sealed class HandleAsync : IAsyncLifetime
{
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
    public async Task WhenSettingsExist_UpdatesAutoResumeAndReturnsSummary()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateDispatchSettings.Handler sut = new(dbContext);
        UpdateDispatchSettings.Command command = new(AutoResumeOnUsageReset: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Success success =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        success.Value.AutoResumeOnUsageReset.ShouldBeFalse();
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
            UpdateDispatchSettings.Handler sut = new(dbContext);
            UpdateDispatchSettings.Command command = new(AutoResumeOnUsageReset: false);
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.AutoResumeOnUsageReset.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSettingsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateDispatchSettings.Handler sut = new(dbContext);
        UpdateDispatchSettings.Command command = new(AutoResumeOnUsageReset: true);

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
