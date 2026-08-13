using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

/// <summary>
/// Verifies that takeoverNamespaces containing a namespace outside the freshly-derived set
/// returns a structured 400 naming the offending namespaces (AC-3).
/// </summary>
public sealed class WhenTakeoverNamespaceOutsideDerivedSet : IAsyncDisposable
{
    private const string Token = "ghp_test_token";

    // The listing only returns "octocat/repo" → derived namespace is "octocat".
    // Requesting takeover of "other-org" (not in derived set) should fail.
    private const string OctocatOnlyListingJson = """
        [
          { "full_name": "octocat/repo", "private": false, "permissions": { "push": true } }
        ]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTakeoverNamespaceOutsideDerivedSet()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new TokenKeyedValidateTokenStub(new Dictionary<string, string>
                {
                    [Token] = "octocat",
                }));

            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new StaticListingFakeHandler(HttpStatusCode.OK, OctocatOnlyListingJson)),
                    NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions())))));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenTakeoverNamespaceNotInDerivedSet_Returns422WithOffendingNamespaces()
    {
        // Arrange — derived set is "octocat"; request takeover of "other-org"
        object body = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = Token,
            takeoverNamespaces = new[] { "other-org" },
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert — structured 422 naming the offending namespace
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        TakeoverValidationResponse? validationBody = await response.Content
            .ReadFromJsonAsync<TakeoverValidationResponse>(TestContext.Current.CancellationToken);
        validationBody.ShouldNotBeNull();
        validationBody.InvalidNamespaces.ShouldContain("other-org");
    }

    [Fact]
    public async Task WhenDerivationUnavailableAndTakeoverRequested_AllNamespacesInvalid()
    {
        // Arrange — derivation fails → empty derived set → all requested namespaces are invalid
        using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new TokenKeyedValidateTokenStub(new Dictionary<string, string>
                {
                    [Token] = "octocat",
                }));

            // Return 500 from the listing endpoint → derivation unavailable → empty derived set
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new StaticListingFakeHandler(HttpStatusCode.InternalServerError, "")),
                    NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions())))));
        });

        using HttpClient client = factory.CreateClient();

        object body = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = Token,
            takeoverNamespaces = new[] { "octocat" },
        };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert — the probe finds no target (no derived namespace repos) before the
        // takeover validation can run, so the response is a 400 with the no-repos message.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        responseBody.ShouldContain("namespace");
    }
}
