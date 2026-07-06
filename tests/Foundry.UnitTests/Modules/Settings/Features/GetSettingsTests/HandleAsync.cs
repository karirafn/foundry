using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GetSettingsTests;

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
    public async Task WhenSettingsExist_ReturnsSettingsSummaryWithDefaultLimits()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GetSettings.Handler sut = new(dbContext);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new GetSettings.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Success success = result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.MaxConcurrent.ShouldBe(GlobalSettings.DefaultMaxConcurrent),
            () => success.Value.TimeoutMinutes.ShouldBe(GlobalSettings.DefaultTimeoutMinutes));
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GetSettings.Handler sut = new(dbContext);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new GetSettings.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Failure failure = result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.NotFoundCode);
    }
}
