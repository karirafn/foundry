using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetWorkerImageBuildArgsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetWorkerImageBuildArgsAsync()
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

    private static WorkerImageConfiguration CreateConfig(
        bool dotnet = false,
        bool angular = false,
        bool chromium = false,
        bool docker = false) =>
        WorkerImageConfiguration.Create(
            imageTag: "foundry-worker:local",
            buildEnabled: true,
            installDotnet: dotnet,
            installAngular: angular,
            installChromium: chromium,
            installDocker: docker)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

    [Fact]
    public async Task WhenConfigurationIsPopulated_ReturnsBuildArgsMatchingToBuildArgs()
    {
        // Arrange
        WorkerImageConfiguration config = CreateConfig(dotnet: true, chromium: true);

        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.UpdateWorkerImageConfiguration(config);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        IReadOnlyDictionary<string, string> expected = config.ToBuildArgs();

        // Act
        IReadOnlyDictionary<string, string> result =
            await sut.GetWorkerImageBuildArgsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsDefaultConfigBuildArgs()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        IReadOnlyDictionary<string, string> expected = WorkerImageConfiguration.Default.ToBuildArgs();

        // Act
        IReadOnlyDictionary<string, string> result =
            await sut.GetWorkerImageBuildArgsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public async Task WhenConfigurationIsPopulated_DoesNotContainInstallGhKey()
    {
        // Arrange
        WorkerImageConfiguration config = CreateConfig(dotnet: true);

        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.UpdateWorkerImageConfiguration(config);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        IReadOnlyDictionary<string, string> result =
            await sut.GetWorkerImageBuildArgsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotContainKey("INSTALL_GH");
    }

    [Fact]
    public async Task WhenConfigurationIsPopulated_DoesNotContainInstallGlabKey()
    {
        // Arrange
        WorkerImageConfiguration config = CreateConfig(dotnet: true);

        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.UpdateWorkerImageConfiguration(config);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        IReadOnlyDictionary<string, string> result =
            await sut.GetWorkerImageBuildArgsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotContainKey("INSTALL_GLAB");
    }
}
