using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Startup;

/// <summary>
/// Verifies that EF Core migrations are applied at startup in all environments,
/// not just Development.
/// </summary>
public sealed class MigrationStartupTests : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public MigrationStartupTests()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenAppStarts_MigrationsAreApplied()
    {
        // Arrange — factory starts a fresh in-memory SQLite database via Migrate(), not EnsureCreated()

        // Act — resolve a DbContext and query the EF migrations history table
        using IServiceScope scope = _factory.Services.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        IEnumerable<string> appliedMigrations = await dbContext.Database
            .GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        // Assert — at least one migration must be applied (the initial migration)
        appliedMigrations.ShouldNotBeEmpty(
            "migrations must be applied at startup in all environments, not only in Development");
    }

    [Fact]
    public async Task WhenAppStarts_AllMigrationsAreApplied()
    {
        // Arrange — factory starts a fresh in-memory SQLite database via Migrate(), not EnsureCreated()

        // Act
        using IServiceScope scope = _factory.Services.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        IEnumerable<string> pendingMigrations = await dbContext.Database
            .GetPendingMigrationsAsync(TestContext.Current.CancellationToken);

        // Assert — no pending migrations remain after startup
        pendingMigrations.ShouldBeEmpty(
            "all pending migrations must be applied before hosted services start");
    }
}
