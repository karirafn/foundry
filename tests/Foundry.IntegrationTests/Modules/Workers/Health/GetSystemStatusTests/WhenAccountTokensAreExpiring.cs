using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.DockerAvailability;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Health.GetSystemStatusTests;

/// <summary>
/// <c>GET /api/system/status</c> seeds the account-token-expiry banner, so a fresh page load shows it
/// without waiting for a broadcast.
/// </summary>
public sealed class WhenAccountTokensAreExpiring : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountTokensAreExpiring()
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
    public async Task WhenNoTokenIsExpiring_ReturnsZeroesAndNoMessage()
    {
        // Arrange
        await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: "current",
            tokenExpiry: new TokenExpiry.On(DateTimeOffset.UtcNow.AddDays(90)));

        // Act
        SystemStatus status = await GetStatusAsync();

        // Assert
        status.ShouldSatisfyAllConditions(
            () => status.ExpiringTokenAccountCount.ShouldBe(0),
            () => status.ExpiredTokenAccountCount.ShouldBe(0),
            () => status.AccountTokenExpiryMessage.ShouldBeNull());
    }

    [Fact]
    public async Task WhenOneTokenIsExpiringAndOneHasExpired_ReturnsBothCountsAndTheCombinedCopy()
    {
        // Arrange
        await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: "expiring",
            tokenExpiry: new TokenExpiry.On(DateTimeOffset.UtcNow.AddDays(3)));
        await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: "expired",
            tokenExpiry: new TokenExpiry.On(DateTimeOffset.UtcNow.AddDays(-1)));

        // Act
        SystemStatus status = await GetStatusAsync();

        // Assert
        status.ShouldSatisfyAllConditions(
            () => status.ExpiringTokenAccountCount.ShouldBe(1),
            () => status.ExpiredTokenAccountCount.ShouldBe(1),
            () => status.AccountTokenExpiryMessage.ShouldBe(
                "1 account has an expired token and 1 has a token expiring within 14 days — enter new ones before monitoring stops."));
    }

    [Fact]
    public async Task WhenAnExpiredAccountIsInvalid_OnlyTheInvalidTokenBannerCountsIt()
    {
        // Arrange
        Guid account = await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: "expired-and-invalid",
            tokenExpiry: new TokenExpiry.On(DateTimeOffset.UtcNow.AddDays(-1)));
        await MarkInvalidAsync(account);

        // Act
        SystemStatus status = await GetStatusAsync();

        // Assert
        status.ShouldSatisfyAllConditions(
            () => status.InvalidTokenAccountCount.ShouldBe(1),
            () => status.ExpiredTokenAccountCount.ShouldBe(0),
            () => status.AccountTokenExpiryMessage.ShouldBeNull());
    }

    private async Task<SystemStatus> GetStatusAsync()
    {
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/system/status", UriKind.Relative),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        SystemStatus? status = await response.Content.ReadFromJsonAsync<SystemStatus>(
            TestContext.Current.CancellationToken);
        status.ShouldNotBeNull();
        return status;
    }

    private async Task MarkInvalidAsync(Guid accountId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        CredentialId id = CredentialId.From(accountId);
        Credential account = await db.Set<Credential>().SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);
        account.MarkTokenInvalid(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
