using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

/// <summary>
/// Verifies the edge case: when the provider listing is unavailable during rotation,
/// the prior namespace associations are kept and resolving repos are marked unreachable,
/// without dropping coverage (AC edge case).
/// </summary>
public sealed class WhenProviderListingUnavailableOnRotate : IAsyncDisposable
{
    private const string OriginalToken = "ghp_original";
    private const string NewToken = "ghp_new_unavailable";

    private const string BroadListingJson = """
        [
          { "full_name": "alice/repo-a", "private": false, "permissions": { "push": true } },
          { "full_name": "bob/repo-b",   "private": false, "permissions": { "push": true } }
        ]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenProviderListingUnavailableOnRotate()
    {
        // The new token maps to no listing entry, so the fake returns "[]" → but we also
        // need the listing call to return a failure to trigger Unavailable outcome.
        // Use a token-keyed handler: new token returns 401 → listing fails → Unavailable.
        Dictionary<string, string> tokenToListing = new()
        {
            [OriginalToken] = BroadListingJson,
            // NewToken deliberately absent → returns [] (success with empty) in TokenKeyedListingFakeHandler.
            // To get Unavailable, we need a non-200 response for NewToken.
        };

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler());

            services.RemoveAll<GitHubHttpClient>();
            // Returns 401 for the new token to simulate a transient listing failure.
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new FailNewTokenListingHandler(OriginalToken, BroadListingJson))));

            // Evaluator marks all resolving repos Unreachable (simulating provider unavailability).
            services.RemoveAll<IRepositoryEligibilityEvaluator>();
            services.AddScoped<IRepositoryEligibilityEvaluator>(_ =>
                new UnreachableEligibilityEvaluator());
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenListingUnavailable_KeepsPriorNamespacesAndMarksReposUnreachable()
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
        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();
        CredentialSummary created = createdResult.Credential;

        // Seed monitored repos under both owners and set them eligible
        Guid aliceRepoId = await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            created.Id,
            slug: "alice/repo-a");
        Guid bobRepoId = await RepositorySeeder.SeedRepositoryAsync(
            _factory,
            created.Id,
            slug: "bob/repo-b");

        await RepositoryEligibilitySeeder.SetEligibleAsync(_factory, aliceRepoId, bobRepoId);

        // Act — rotate to a new token whose listing call fails (simulating transient error)
        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = NewToken,
        };

        HttpResponseMessage updateResponse = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert — response is 200, coverage NOT dropped
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Namespaces must be unchanged (both alice and bob retained)
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Credential? credential = await dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(created.Id),
                TestContext.Current.CancellationToken);
        credential.ShouldNotBeNull();
        credential.Namespaces.Count.ShouldBe(2, "prior namespaces must be retained on transient failure");
        credential.Namespaces.ShouldContain(ns => ns.Value == "alice");
        credential.Namespaces.ShouldContain(ns => ns.Value == "bob");

        // Both repos changed: eligible → unreachable (recheck marker)
        CredentialUpdateResult? result = await updateResponse.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.AffectedRepositories.Count.ShouldBe(2, "both repos changed from eligible to unreachable");
        result.AffectedRepositories.ShouldAllBe(r => r.NewStatus == "unreachable");
        result.AffectedRepositories.ShouldAllBe(r => r.PreviousStatus == "eligible");
    }

    /// <summary>
    /// Returns a successful listing for the original token but a 401 for any other token,
    /// simulating a transient provider failure for the new token.
    /// Probe POSTs always return 422 (Granted) so the write-permission probe passes during
    /// account creation regardless of which token is in use.
    /// </summary>
    private sealed class FailNewTokenListingHandler(
        string validToken,
        string validListingJson) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Probe POSTs (/git/refs, /issues, /pulls) always return 422 (Granted)
            // so write-permission probing succeeds regardless of the token used.
            if (StaticListingFakeHandler.IsProbePost(request))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
            }

            string token = string.Empty;
            if (request.Headers.TryGetValues("Authorization", out IEnumerable<string>? authValues))
            {
                string bearer = authValues.FirstOrDefault() ?? string.Empty;
                token = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? bearer["Bearer ".Length..]
                    : string.Empty;
            }

            if (token == validToken)
            {
                HttpResponseMessage success = new(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        validListingJson,
                        System.Text.Encoding.UTF8,
                        "application/json"),
                };
                return Task.FromResult(success);
            }

            HttpResponseMessage unauthorized = new(HttpStatusCode.Unauthorized);
            return Task.FromResult(unauthorized);
        }
    }
}
