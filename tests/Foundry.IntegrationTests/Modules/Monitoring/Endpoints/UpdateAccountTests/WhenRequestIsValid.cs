using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

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

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

public sealed class WhenRequestIsValid : IAsyncDisposable
{
    private const string InitialAccountName = "octocat";

    // One writable repo under "octocat" so namespace derivation and probing succeed during seeding.
    private const string OctocatListingJson = """
        [{"full_name":"octocat/repo","private":false,"permissions":{"push":true}}]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRequestIsValid()
    {
        ValidateToken.Response validResponse = new(
            Kind: ValidateToken.Kinds.Authenticated,
            AccountName: InitialAccountName,
            MissingScopes: [],
            DetectedProvider: null);
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(validResponse)));

            // Probe-aware: listing GETs return repos; probe POSTs return 422 (Granted).
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new StaticListingFakeHandler(HttpStatusCode.OK, OctocatListingJson)),
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
    public async Task WhenNoTokenSupplied_KeepsExistingName()
    {
        // Arrange
        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_original_token",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();
        CredentialSummary created = createdResult.Credential;

        object updateBody = new
        {
            baseUrl = "https://github.com",
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        CredentialUpdateResult? result = await response.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Credential.Name.ShouldBe(InitialAccountName);
    }

    [Fact]
    public async Task WhenTokenProvided_RenamesAccountToResolvedIdentity()
    {
        // Arrange — token-keyed routing: original token yields InitialAccountName, new token yields NewIdentity.
        const string OriginalToken = "ghp_original_token";
        const string NewToken = "ghp_new_token";
        const string NewIdentity = "new-identity";

        TokenKeyedStub tokenKeyedStub = new(new Dictionary<string, string>
        {
            [OriginalToken] = InitialAccountName,
            [NewToken] = NewIdentity,
        });

        // Each token returns repos under a distinct namespace so derivation does not conflict.
        Dictionary<string, string> tokenToListing = new()
        {
            [OriginalToken] = """[{"full_name":"octocat/repo","private":false,"permissions":{"push":true}}]""",
            [NewToken] = """[{"full_name":"new-identity/repo","private":false,"permissions":{"push":true}}]""",
        };

        using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddSingleton<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(tokenKeyedStub);

            // Probe-aware: each token returns a different listing; probe POSTs return 422 (Granted).
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new TokenKeyedListingFakeHandler(tokenToListing)),
                    NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions())))));
        });
        using HttpClient client = factory.CreateClient();

        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = OriginalToken,
        };

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();
        CredentialSummary created = createdResult.Credential;

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = NewToken,
        };

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        CredentialUpdateResult? result = await response.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Credential.Name.ShouldBe(NewIdentity);
    }

    [Fact]
    public async Task WhenTokenProvided_UpdatesToken()
    {
        // Arrange
        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_original_token",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();
        CredentialSummary created = createdResult.Credential;

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = "ghp_new_token",
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        CredentialUpdateResult? result = await response.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Credential.ShouldSatisfyAllConditions(
            () => result.Credential.HasToken.ShouldBeTrue(),
            () => result.Credential.Name.ShouldBe(InitialAccountName));
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
                Kind: ValidateToken.Kinds.Authenticated,
                AccountName: accountName,
                MissingScopes: [],
                DetectedProvider: null);
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
