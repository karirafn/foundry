using System.Net;
using System.Text;

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
/// Verifies that a provider listing error is mapped to a 400 Bad Request response.
/// The endpoint error map routes non-NotFound errors (including provider failures) to BadRequest.
/// </summary>
public sealed class WhenProviderListingFails : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenProviderListingFails()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(new HttpClient(new FailingListingHandler()), NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions())))));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsBadRequest()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories/available-repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // Returns a non-success status code so that GitHubHttpClient.ListRepositoriesAsync returns a Failure result.
    private sealed class FailingListingHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Service unavailable", Encoding.UTF8, "text/plain"),
            };
            return Task.FromResult(response);
        }
    }
}
