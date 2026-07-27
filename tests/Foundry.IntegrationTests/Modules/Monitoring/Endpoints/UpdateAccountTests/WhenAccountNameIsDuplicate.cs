using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

/// <summary>
/// Verifies that updating a credential to use a token that resolves to the same name as
/// another credential now succeeds — the (base_url, name) unique index was dropped in
/// favour of per-namespace scoping.
/// </summary>
public sealed class WhenAccountNameIsDuplicate : IAsyncDisposable
{
    // Token-keyed routing: each token maps to a fixed account name.
    // ghp_first_token  → first-user  (create first account, namespace "first-user")
    // ghp_second_token → second-user (create second account, namespace "second-user")
    // ghp_colliding_token → first-user (update second account; was a conflict, now allowed)
    private const string FirstToken = "ghp_first_token";
    private const string SecondToken = "ghp_second_token";
    private const string CollidingToken = "ghp_colliding_token";
    private const string FirstAccountName = "first-user";
    private const string SecondAccountName = "second-user";

    // Each token returns repos under a different namespace so derivation does not conflict.
    private static readonly Dictionary<string, string> TokenToListing = new()
    {
        [FirstToken] = """[{"full_name":"first-user/repo","private":false,"permissions":{"push":true}}]""",
        [SecondToken] = """[{"full_name":"second-user/repo","private":false,"permissions":{"push":true}}]""",
        [CollidingToken] = """[{"full_name":"first-user/repo","private":false,"permissions":{"push":true}}]""",
    };

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly TokenKeyedStub _stub;

    public WhenAccountNameIsDuplicate()
    {
        _stub = new TokenKeyedStub(new Dictionary<string, string>
        {
            [FirstToken] = FirstAccountName,
            [SecondToken] = SecondAccountName,
            [CollidingToken] = FirstAccountName,
        });
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(_ => _stub);

            // Probe-aware: each token returns a different listing; probe POSTs return 422 (Granted).
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new TokenKeyedListingFakeHandler(TokenToListing))));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsOk()
    {
        // Arrange — create two accounts with distinct names, then update the second to use
        // a token that resolves to the first account's name. This is now allowed — the
        // unique-by-identity constraint was replaced by per-namespace scoping.
        object firstBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = FirstToken,
        };

        object secondBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = SecondToken,
        };

        await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            firstBody,
            TestContext.Current.CancellationToken);

        HttpResponseMessage secondResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            secondBody,
            TestContext.Current.CancellationToken);

        CredentialCreationResult? secondResult = await secondResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        secondResult.ShouldNotBeNull();

        // Update second account with a token that resolves to the first account's name
        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = CollidingToken,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{secondResult.Credential.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert — the update succeeds; same-name credentials are now valid.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WhenNoTokenSupplied_NameUnchangedDoesNotConflict()
    {
        // Arrange — updating without a token keeps the existing name; no conflict expected
        ValidateToken.Response validResponse = new(
            IsValid: true,
            IsAuthFailure: false,
            ScopesVerified: true,
            MissingScopes: [],
            AccountName: "octocat");

        using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(validResponse)));

            // Probe-aware: listing GETs return repos; probe POSTs return 422 (Granted).
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new StaticListingFakeHandler(
                        System.Net.HttpStatusCode.OK,
                        """[{"full_name":"octocat/repo","private":false,"permissions":{"push":true}}]"""))));
        });
        using HttpClient client = factory.CreateClient();

        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();

        object updateBody = new { baseUrl = "https://github.com" };

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/api/accounts/{createdResult.Credential.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Routes each ValidateToken call to a fixed account name based on the token value.
    // This is robust against extra internal calls — the result depends solely on the token.
    private sealed class TokenKeyedStub(Dictionary<string, string> tokenToName)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken)
        {
            string accountName = tokenToName.TryGetValue(query.Token, out string? name)
                ? name
                : "default-user";
            ValidateToken.Response response = new(
                IsValid: true,
                IsAuthFailure: false,
                ScopesVerified: true,
                MissingScopes: [],
                AccountName: accountName);
            return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
        }
    }

    private sealed class StubValidateTokenHandler(Result<ValidateToken.Response> result)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
