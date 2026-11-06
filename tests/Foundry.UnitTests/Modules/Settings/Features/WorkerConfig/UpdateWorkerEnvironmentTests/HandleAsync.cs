using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features.WorkerConfig;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.WorkerConfig.UpdateWorkerEnvironmentTests;

public sealed class HandleAsync : IAsyncLifetime
{
    private const int ValidMemoryLimitMb = 4096;
    private const double ValidCpuLimit = 4.0;
    private const int ValidPidsLimit = 1024;

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

    private async Task SeedSettingsAsync()
    {
        await using FoundryDbContext seedDb = CreateDbContext();
        seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static UpdateWorkerEnvironment.Command CreateCommand(
        int memoryLimitMb = ValidMemoryLimitMb,
        double cpuLimit = ValidCpuLimit,
        int pidsLimit = ValidPidsLimit) =>
        new(memoryLimitMb, cpuLimit, pidsLimit);

    [Fact]
    public async Task WhenValid_ReturnsSummaryWithNewLimits()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateWorkerEnvironment.Handler sut = new(dbContext);
        UpdateWorkerEnvironment.Command command = CreateCommand();

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        GlobalSettingsSummary summary = result.Match(v => v, e => throw new InvalidOperationException(e.Message));
        summary.ShouldSatisfyAllConditions(
            () => summary.MemoryLimitMb.ShouldBe(ValidMemoryLimitMb),
            () => summary.CpuLimit.ShouldBe(ValidCpuLimit),
            () => summary.PidsLimit.ShouldBe(ValidPidsLimit));
    }

    [Fact]
    public async Task WhenValid_PersistsLimits()
    {
        // Arrange
        await SeedSettingsAsync();
        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateWorkerEnvironment.Handler sut = new(dbContext);
            UpdateWorkerEnvironment.Command command = CreateCommand(memoryLimitMb: 2048);

            // Act
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext verifyDb = CreateDbContext();
        GlobalSettings persisted = await verifyDb.Set<GlobalSettings>()
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        persisted.WorkerResourceLimits.MemoryLimitMb.ShouldBe(2048);
    }

    [Fact]
    public async Task WhenSettingsDoNotExist_ReturnsNotFound()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateWorkerEnvironment.Handler sut = new(dbContext);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            CreateCommand(),
            TestContext.Current.CancellationToken);

        // Assert
        Error error = ((Result<GlobalSettingsSummary>.Failure)result).Error;
        error.Code.ShouldBe(SettingsErrors.NotFoundCode);
    }

    [Theory]
    [InlineData(1, ValidCpuLimit, ValidPidsLimit, SettingsErrors.InvalidMemoryLimitCode)]
    [InlineData(ValidMemoryLimitMb, 0.0, ValidPidsLimit, SettingsErrors.InvalidCpuLimitCode)]
    [InlineData(ValidMemoryLimitMb, ValidCpuLimit, 1, SettingsErrors.InvalidPidsLimitCode)]
    public async Task WhenLimitsOutOfRange_ReturnsFieldError(
        int memoryLimitMb,
        double cpuLimit,
        int pidsLimit,
        string expectedCode)
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateWorkerEnvironment.Handler sut = new(dbContext);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            CreateCommand(memoryLimitMb, cpuLimit, pidsLimit),
            TestContext.Current.CancellationToken);

        // Assert
        ((Result<GlobalSettingsSummary>.Failure)result).Error.Code.ShouldBe(expectedCode);
    }
}
