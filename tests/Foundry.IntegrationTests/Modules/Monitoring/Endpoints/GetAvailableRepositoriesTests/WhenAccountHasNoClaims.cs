using System.Net;
using System.Net.Http.Json;
using System.Text;

using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetAvailableRepositoriesTests;

/// <summary>
/// Verifies that an account with no namespace claims returns HasClaims=false and an empty repository list,
/// even when the provider listing contains repositories.
/// </summary>
public sealed class WhenAccountHasNoClaims : IAsyncDisposable
{
    private const string GitHubListingJson = """
        [
          { "full_name": "owner/some-repo", "private": false, "permissions": { "push": true } }
        ]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountHasNoClaims()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(new HttpClient(new ListingFakeHandler(HttpStatusCode.OK, GitHubListingJson)), NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions())))));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsHasClaimsFalseAndEmptyRepositories()
    {
        // Arrange — seed an account with no namespace claims
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories/available-repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AvailableRepositoriesResponse? result = await response.Content
            .ReadFromJsonAsync<AvailableRepositoriesResponse>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.HasClaims.ShouldBeFalse();
        result.Repositories.ShouldBeEmpty();
    }

    private sealed class ListingFakeHandler(HttpStatusCode statusCode, string responseBody) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
