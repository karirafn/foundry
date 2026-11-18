using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.AccountTokenBannerTests;

/// <summary>
/// Deleting the only account whose token is expiring clears the account-token-expiry banner immediately.
/// </summary>
public sealed class WhenAnExpiringAccountIsDeleted : IAsyncDisposable
{
    private readonly CapturingSystemNotificationBroadcaster _broadcaster = new();
    private readonly FoundryWebAppFactory _factory;

    public WhenAnExpiringAccountIsDeleted()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<ISystemNotificationBroadcaster>();
            services.AddSingleton<ISystemNotificationBroadcaster>(_broadcaster);
        });
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task TheExpiryBannerIsBroadcastInactive()
    {
        // Arrange
        Guid account = await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: "expiring",
            tokenExpiry: new TokenExpiry.On(DateTimeOffset.UtcNow.AddDays(3)));
        using HttpClient client = _factory.CreateClient();

        // Act
        HttpResponseMessage response = await client.DeleteAsync(
            new Uri($"/api/accounts/{account}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        _broadcaster.SentNotifications
            .Where(n => n.Category == AccountTokenExpiryNotification.Category)
            .Last()
            .ShouldBe(new SystemNotification(AccountTokenExpiryNotification.Category, IsActive: false, Message: string.Empty));
    }
}
