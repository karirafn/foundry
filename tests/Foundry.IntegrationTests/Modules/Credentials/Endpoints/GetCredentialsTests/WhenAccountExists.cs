using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.GetCredentialsTests;

public sealed class WhenAccountExists : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountExists()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedDefaultAccountAsync()
    {
        // ClaudeAccountSeeder is a hosted service and is removed in tests — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ClaudeAccount account = ClaudeAccount.Create();
        dbContext.Set<ClaudeAccount>().Add(account);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedBlockedAccountAsync(DateTimeOffset nextProbeAt)
    {
        // No endpoint can produce a blocked spend state — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend(nextProbeAt);
        dbContext.Set<ClaudeAccount>().Add(account);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsOkWithAccountSummary()
    {
        // Arrange
        await SeedDefaultAccountAsync();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ClaudeAccountSummary? summary = await response.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("ApiKey"),
            () => summary.OAuthStatus.ShouldBe("NotConfigured"),
            () => summary.OAuthAccountEmail.ShouldBeNull(),
            () => summary.OAuthAccountOrgName.ShouldBeNull(),
            () => summary.NextProbeAt.ShouldBeNull());
    }

    [Fact]
    public async Task WhenSpendIsBlocked_NextProbeAtIsReturned()
    {
        // Arrange
        DateTimeOffset nextProbeAt = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        await SeedBlockedAccountAsync(nextProbeAt);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ClaudeAccountSummary? summary = await response.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.NextProbeAt.ShouldBe(nextProbeAt);
    }
}
