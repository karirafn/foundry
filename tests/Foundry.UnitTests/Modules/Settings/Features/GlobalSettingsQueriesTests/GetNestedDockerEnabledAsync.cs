using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetNestedDockerEnabledAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetNestedDockerEnabledAsync()
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
    public async Task WhenSwitchIsOn_ReturnsTrue()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.UpdateNestedDocker(true);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        bool result = await sut.GetNestedDockerEnabledAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenSwitchIsOff_ReturnsFalse()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        bool result = await sut.GetNestedDockerEnabledAsync(TestContext.Current.CancellationToken);

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
        bool result = await sut.GetNestedDockerEnabledAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }
}
