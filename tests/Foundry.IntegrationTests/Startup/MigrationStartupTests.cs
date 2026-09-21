using Foundry.WebApi.Persistence;

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

    // Creating the HttpClient triggers WebApplicationFactory to boot the host,
    // which exercises the Program.cs startup migration path under test.
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
    public async Task WhenAppStarts_AllMigrationsAreApplied()
    {
        // Arrange
        using IServiceScope scope = _factory.Services.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        // Act
        IEnumerable<string> pendingMigrations = await dbContext.Database
            .GetPendingMigrationsAsync(TestContext.Current.CancellationToken);

        // Assert
        pendingMigrations.ShouldBeEmpty(
            "all pending migrations must be applied before hosted services start");
    }
}
