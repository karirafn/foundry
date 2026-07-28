using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.UpdateAuthModeTests;

public sealed class WhenPayloadIsValid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenPayloadIsValid()
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

    [Fact]
    public async Task WhenApiKeyMode_ReturnsOkWithApiKeySummary()
    {
        // Arrange
        await SeedDefaultAccountAsync();
        object body = new { mode = "api_key", apiKey = "sk-ant-test-key-abc123" };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/credentials/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ClaudeAccountSummary? summary = await response.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("ApiKey"),
            () => summary.OAuthStatus.ShouldBe("NotConfigured"));
    }

    [Fact]
    public async Task WhenOAuthMode_ReturnsOkWithOAuthSummary()
    {
        // Arrange
        await SeedDefaultAccountAsync();
        object body = new { mode = "oauth" };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/credentials/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ClaudeAccountSummary? summary = await response.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("OAuth"),
            () => summary.OAuthStatus.ShouldBe("ReLoginNeeded"));
    }
}
