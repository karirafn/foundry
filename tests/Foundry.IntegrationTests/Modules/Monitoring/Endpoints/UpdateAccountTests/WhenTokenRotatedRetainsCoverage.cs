using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

/// <summary>
/// Verifies that when the new token retains all prior owner coverage,
/// all repos remain eligible and the affected list is empty (AC3).
/// </summary>
public sealed class WhenTokenRotatedRetainsCoverage : IAsyncDisposable
{
    private const string OriginalToken = "ghp_original";
    private const string RetainedToken = "ghp_retained";

    private const string FullCoverageListingJson = """
        [
          { "full_name": "alice/repo-a", "private": false, "permissions": { "push": true } },
          { "full_name": "bob/repo-b",   "private": false, "permissions": { "push": true } }
        ]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenRotatedRetainsCoverage()
    {
        Dictionary<string, string> tokenToListing = new()
        {
            [OriginalToken] = FullCoverageListingJson,
            [RetainedToken] = FullCoverageListingJson,
        };

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler());

            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(new HttpClient(new TokenKeyedListingFakeHandler(tokenToListing))));

            // Both repos stay eligible after rotation — no status change expected.
            services.RemoveAll<IRepositoryEligibilityEvaluator>();
            services.AddScoped<IRepositoryEligibilityEvaluator>(_ =>
                new AssignedEligibilityEvaluator(new Dictionary<string, RepositoryEligibility>
                {
                    ["alice/repo-a"] = new RepositoryEligibility.Eligible(),
                    ["bob/repo-b"] = new RepositoryEligibility.Eligible(),
                }));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenCoverageRetained_AffectedListIsEmpty()
    {
        // Arrange — create with broad token (alice + bob namespaces)
        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = OriginalToken,
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        CredentialSummary? created = await createResponse.Content
            .ReadFromJsonAsync<CredentialSummary>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();

        // Seed monitored repos and set both eligible
        Guid aliceRepoId = await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            created.Id,
            slug: "alice/repo-a");
        Guid bobRepoId = await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            created.Id,
            slug: "bob/repo-b");

        await RepositoryEligibilitySeeder.SetEligibleAsync(_factory, aliceRepoId, bobRepoId);

        // Act — rotate to a token that retains full coverage
        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = RetainedToken,
        };

        HttpResponseMessage updateResponse = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert — no repos changed, affected list empty (AC3)
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        CredentialUpdateResult? result = await updateResponse.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.AffectedRepositories.ShouldBeEmpty();
    }
}
