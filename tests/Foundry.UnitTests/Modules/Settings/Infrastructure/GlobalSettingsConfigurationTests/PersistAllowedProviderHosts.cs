using Foundry.Modules.Settings.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Infrastructure.GlobalSettingsConfigurationTests;

public sealed class PersistAllowedProviderHosts : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistAllowedProviderHosts()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenAllowedProviderHostsPersisted_RoundTripsCorrectly()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdateAllowedProviderHosts(["git.example.com", "gitlab.company.org"]);

        _dbContext.Set<GlobalSettings>().Add(settings);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        GlobalSettings? result = await _dbContext
            .Set<GlobalSettings>()
            .FindAsync([settings.Id], TestContext.Current.CancellationToken);

        // Assert
        GlobalSettings reloaded = result.ShouldNotBeNull();
        reloaded.AllowedProviderHosts.ShouldBe(["git.example.com", "gitlab.company.org"]);
    }

    [Fact]
    public async Task WhenEmptyListPersisted_RoundTripsAsEmptyList()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        _dbContext.Set<GlobalSettings>().Add(settings);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        GlobalSettings? result = await _dbContext
            .Set<GlobalSettings>()
            .FindAsync([settings.Id], TestContext.Current.CancellationToken);

        // Assert
        GlobalSettings reloaded = result.ShouldNotBeNull();
        reloaded.AllowedProviderHosts.ShouldBeEmpty();
    }
}
