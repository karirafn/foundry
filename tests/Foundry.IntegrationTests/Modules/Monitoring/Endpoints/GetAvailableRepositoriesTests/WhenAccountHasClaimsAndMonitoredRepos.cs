using System.Net;
using System.Net.Http.Json;
using System.Text;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetAvailableRepositoriesTests;

/// <summary>
/// Verifies that the handler filters provider repos to the claimed namespace,
/// marks already-monitored repos, and preserves the CanPush flag.
/// </summary>
public sealed class WhenAccountHasClaimsAndMonitoredRepos : IAsyncDisposable
{
    // Three repos: two under the claimed "owner" namespace (one writable, one read-only),
    // and one under an unclaimed "other-org" namespace.
    private const string GitHubListingJson = """
        [
          { "full_name": "owner/writable-repo", "private": false, "permissions": { "push": true } },
          { "full_name": "owner/readonly-repo", "private": true, "permissions": { "push": false } },
          { "full_name": "other-org/unclaimed-repo", "private": false, "permissions": { "push": true } }
        ]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountHasClaimsAndMonitoredRepos()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(new HttpClient(new ListingFakeHandler(HttpStatusCode.OK, GitHubListingJson))));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsOnlyClaimedRepos_WithCorrectMonitoredAndCanPushFlags()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        await SeedMonitoredRepositoryAsync("owner/writable-repo", "github.com");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories/available-repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AvailableRepositoriesResponse? result = await response.Content
            .ReadFromJsonAsync<AvailableRepositoriesResponse>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.HasClaims.ShouldBeTrue();
        result.Repositories.Count.ShouldBe(2);
        result.Repositories.ShouldSatisfyAllConditions(
            () => result.Repositories.ShouldNotContain(r => r.Slug == "other-org/unclaimed-repo"),
            () => result.Repositories.ShouldContain(r => r.Slug == "owner/writable-repo" && r.CanPush && r.IsMonitored),
            () => result.Repositories.ShouldContain(r => r.Slug == "owner/readonly-repo" && !r.CanPush && !r.IsMonitored));
    }

    private async Task SeedMonitoredRepositoryAsync(string slug, string host)
    {
        // No HTTP endpoint creates a monitored repository directly with a specific slug and host,
        // so we seed via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();
        MonitoredRepository repository = MonitoredRepository.Create(repositorySlug, host, pollInterval: null);
        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(CancellationToken.None);
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
