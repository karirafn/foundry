using Foundry.IntegrationTests.Smoke;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Startup;

/// <summary>
/// Every test host used to migrate its own database and serve plain HTTP, so each one logged the same
/// migration and HTTPS redirection warnings. A host booted on the migrated template logs neither.
/// </summary>
public sealed class HostStartupWarningTests
{
    private const string MigrationsCategory = "Microsoft.EntityFrameworkCore.Migrations";
    private const string HttpsPolicyCategoryPrefix = "Microsoft.AspNetCore.HttpsPolicy";

    [Fact]
    public async Task WhenHostBootsAndServesARequest_NoMigrationOrHttpsPolicyWarningIsLogged()
    {
        // Arrange
        using CapturedLogs logs = new();
        await using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(
            services => services.AddSingleton<ILoggerProvider>(logs));
        using HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/settings", UriKind.Relative), TestContext.Current.CancellationToken);

        // Assert
        logs.Entries
            .Where(entry => entry.Level >= LogLevel.Warning)
            .Where(entry => entry.Category == MigrationsCategory
                || entry.Category.StartsWith(HttpsPolicyCategoryPrefix, StringComparison.Ordinal))
            .Select(entry => $"{entry.Category}: {entry.Message}")
            .ShouldBeEmpty();
    }
}
