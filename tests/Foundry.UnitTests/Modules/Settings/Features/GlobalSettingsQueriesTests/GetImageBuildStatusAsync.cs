using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetImageBuildStatusAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetImageBuildStatusAsync()
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
    public async Task WhenNoSettingsExist_ReturnsIdle()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        ImageBuildStatus result = await sut.GetImageBuildStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(ImageBuildStatus.Idle);
    }

    [Fact]
    public async Task WhenImageBuildIsBuilding_ReturnsBuilding()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.BeginImageBuild();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        ImageBuildStatus result = await sut.GetImageBuildStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(ImageBuildStatus.Building);
    }

    [Fact]
    public async Task WhenImageBuildFailed_ReturnsFailed()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.BeginImageBuild();
            settings.FailImageBuild("Build error output");
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        ImageBuildStatus result = await sut.GetImageBuildStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(ImageBuildStatus.Failed);
    }
}
