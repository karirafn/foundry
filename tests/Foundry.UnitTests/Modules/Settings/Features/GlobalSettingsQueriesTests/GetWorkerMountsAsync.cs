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

public sealed class GetWorkerMountsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetWorkerMountsAsync()
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
    public async Task WhenSettingsExistWithMounts_ReturnsStoredMounts()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            WorkerMounts mounts = new WorkerMountsBuilder()
                .WithMount("/skills", "/home/user/skills", readOnly: true)
                .WithMount("/cache", "/var/cache/foundry", readOnly: false)
                .Build()
                .Match(v => v, e => throw new InvalidOperationException(e.Message));
            settings.UpdateWorkerEnvironment(WorkerResourceLimits.Default, mounts);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        IReadOnlyList<WorkerMountEntry> result = await sut.GetWorkerMountsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(
        [
            new WorkerMountEntry("/skills", "/home/user/skills", true),
            new WorkerMountEntry("/cache", "/var/cache/foundry", false),
        ]);
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsEmpty()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        IReadOnlyList<WorkerMountEntry> result = await sut.GetWorkerMountsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }
}
