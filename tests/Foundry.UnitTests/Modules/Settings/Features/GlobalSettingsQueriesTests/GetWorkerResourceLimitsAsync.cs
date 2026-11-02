using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetWorkerResourceLimitsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetWorkerResourceLimitsAsync()
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
    public async Task WhenSettingsExistWithCustomLimits_ReturnsStoredLimits()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            WorkerResourceLimits limits = new WorkerResourceLimitsBuilder()
                .WithMemoryLimitMb(4096)
                .WithCpuLimit(4.5)
                .WithPidsLimit(1024)
                .Build()
                .Match(v => v, e => throw new InvalidOperationException(e.Message));
            settings.UpdateWorkerEnvironment(limits, WorkerMounts.Default);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        WorkerResourceLimitsSnapshot result = await sut.GetWorkerResourceLimitsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(new WorkerResourceLimitsSnapshot(4096, 4.5, 1024));
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsDefaultLimits()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        WorkerResourceLimitsSnapshot result = await sut.GetWorkerResourceLimitsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(new WorkerResourceLimitsSnapshot(
            WorkerResourceLimits.Default.MemoryLimitMb,
            WorkerResourceLimits.Default.CpuLimit,
            WorkerResourceLimits.Default.PidsLimit));
    }
}
