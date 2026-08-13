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

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.DeleteAccountTests;

public sealed class WhenAccountExists : IAsyncDisposable
{
    // One writable repo under "octocat" so namespace derivation and probing succeed during seeding.
    private const string OctocatListingJson = """
        [{"full_name":"octocat/repo","private":false,"permissions":{"push":true}}]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountExists()
    {
        ValidateToken.Response validResponse = new(
            Kind: ValidateToken.Kinds.Authenticated,
            AccountName: "octocat",
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
    public async Task ReturnsNoContent()
    {
        // Arrange
        object createBody = new
        {
            name = "My GitHub",
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();
        CredentialSummary created = createdResult.Credential;

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AccountNoLongerAppearsInGetAccounts()
    {
        // Arrange
        object createBody = new
        {
            name = "My GitHub",
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();
        CredentialSummary created = createdResult.Credential;

        await _client.DeleteAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage getResponse = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<CredentialSummary>? accounts = await getResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<CredentialSummary>>(TestContext.Current.CancellationToken);
        accounts.ShouldNotBeNull();
        accounts.ShouldNotContain(a => a.Id == created.Id);
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
