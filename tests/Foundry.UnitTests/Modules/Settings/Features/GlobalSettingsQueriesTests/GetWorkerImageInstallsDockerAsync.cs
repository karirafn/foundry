using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetWorkerImageInstallsDockerAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetWorkerImageInstallsDockerAsync()
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
    public async Task WhenInstallDockerIsTrue_ReturnsTrue()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            WorkerImageConfiguration config = WorkerImageConfiguration.Create(
                imageTag: "foundry-worker:local",
                buildEnabled: true,
                installDotnet: false,
                installAngular: false,
                installChromium: false,
                installDocker: true)
                .Match(v => v, e => throw new InvalidOperationException(e.Message));
            settings.UpdateWorkerImageConfiguration(config);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        bool result = await sut.GetWorkerImageInstallsDockerAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenInstallDockerIsFalse_ReturnsFalse()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            WorkerImageConfiguration config = WorkerImageConfiguration.Create(
                imageTag: "foundry-worker:local",
                buildEnabled: true,
                installDotnet: false,
                installAngular: false,
                installChromium: false,
                installDocker: false)
                .Match(v => v, e => throw new InvalidOperationException(e.Message));
            settings.UpdateWorkerImageConfiguration(config);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        bool result = await sut.GetWorkerImageInstallsDockerAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsFalse()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        bool result = await sut.GetWorkerImageInstallsDockerAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }
}
