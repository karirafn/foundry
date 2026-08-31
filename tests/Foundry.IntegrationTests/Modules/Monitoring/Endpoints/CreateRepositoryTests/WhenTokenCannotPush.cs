using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;
using Foundry.IntegrationTests.Modules.Monitoring.Endpoints;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateRepositoryTests;

public sealed class WhenTokenCannotPush : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenCannotPush()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            // For GitHub accounts, write-permission is probed via IGitHubWriteProber (backed by
            // GitHubHttpClient). Return 403 from all probe POSTs so the evaluator classifies the
            // repo as Ineligible with a cannot-push violation — no IIssueProviderFactory needed.
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new ProbeBlockedFakeHandler()),
                    NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsIneligibleWithCannotPushViolation()
    {
        // Arrange — set namespace so resolver can cover the repo
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "No Push Org");
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        object body = new
        {
            slug = "owner/no-push-repo",
            pollIntervalSeconds = 300,
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        RepositorySummary? repository = await response.Content
            .ReadFromJsonAsync<RepositorySummary>(TestContext.Current.CancellationToken);
        repository.ShouldNotBeNull();
        repository.Eligibility.ShouldNotBeNull();
        repository.Eligibility.ShouldSatisfyAllConditions(
            () => repository.Eligibility.Status.ShouldBe("ineligible"),
            () => repository.Eligibility.Violations.ShouldHaveSingleItem(),
            () => repository.Eligibility.Violations[0].Rule.ShouldBe("cannot-push:owner/no-push-repo"),
            () => repository.Eligibility.Violations[0].Description.ShouldBe("token cannot push to owner/no-push-repo"));
    }

}
