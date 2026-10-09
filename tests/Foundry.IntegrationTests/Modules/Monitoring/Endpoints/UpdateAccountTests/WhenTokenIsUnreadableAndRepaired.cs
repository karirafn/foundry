using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

/// <summary>
/// Verifies AC4: a credential whose token column contains garbage ciphertext
/// (unreadable by the active key ring) can be repaired by supplying a fresh
/// valid token via PUT /api/accounts/{id}.
///
/// Setup mirrors WhenRequestIsValid: the account is created through POST /api/accounts
/// so its id and credential_namespaces rows are EF-native. After creation, only the
/// token column is corrupted via raw SQL UPDATE (no WHERE — there is exactly one row),
/// simulating a lost key ring without touching the id or namespace state.
/// </summary>
public sealed class WhenTokenIsUnreadableAndRepaired : IAsyncDisposable
{
    // Valid base64 that the ephemeral key ring cannot unprotect.
    private const string GarbageBase64Token =
        "AAAA/totally/random+bytes+that+no+key+ring+can+ever+decrypt==";

    private const string FreshToken = "ghp_fresh_valid_token";
    private const string RepairedAccountName = "repaired-user";

    // One writable repo under "repaired-user" so namespace derivation passes during
    // both the initial create and the repair PUT.
    private const string RepairedUserListingJson =
        """[{"full_name":"repaired-user/repo","private":false,"permissions":{"push":true}}]""";

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenIsUnreadableAndRepaired()
    {
        ValidateToken.Response validResponse = new(
            Kind: ValidateToken.Kinds.Authenticated,
            AccountName: RepairedAccountName,
            MissingScopes: [],
            DetectedProvider: null);

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            // Stub ValidateToken so both the create and the repair token resolve
            // successfully without a real provider call.
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new ReturnsAlwaysOkStub(validResponse));

            // Stub the GitHub HTTP client: return the repaired-user listing for GETs,
            // return 422 for probe POSTs (Granted, so write-permission probing passes).
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new StaticListingFakeHandler(HttpStatusCode.OK, RepairedUserListingJson)),
                    NullLogger<GitHubHttpClient>.Instance,
                    new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
                    new InMemoryProviderRateBudget(),
                    TimeProvider.System));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Creates a real account through POST /api/accounts (so its id and namespace rows
    /// are EF-native), then overwrites only the token column with garbage ciphertext via
    /// raw SQL UPDATE. No WHERE clause avoids Guid TEXT format mismatches — there is
    /// exactly one credential row in the per-class test database at this point.
    /// This mirrors the pattern in GetAvailableRepositoriesHandlerTests.SeedGitHubAccountWithGarbageTokenAsync.
    /// </summary>
    private async Task<Guid> SeedAccountWithCorruptedTokenAsync()
    {
        // Create via POST so the account has an EF-native id and credential_namespaces rows.
        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_placeholder_token",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();
        Guid accountId = createdResult.Credential.Id;

        // Overwrite the token column with garbage ciphertext. No WHERE clause — there is
        // exactly one row in this per-class database. This simulates a lost key ring:
        // ProviderTokenConverter.Read will throw CryptographicException and materialize
        // ProviderToken.Unreadable.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE accounts SET token = {0}",
            GarbageBase64Token);

        return accountId;
    }

    [Fact]
    public async Task WhenFreshTokenSupplied_UpdateAccountReturnsPresent()
    {
        // Arrange — create a real account via POST, then corrupt only its token column
        // to simulate the "lost key ring" scenario (AC4).
        Guid accountId = await SeedAccountWithCorruptedTokenAsync();

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = FreshToken,
        };

        // Act — supply a fresh valid token via PUT /api/accounts/{id}.
        // UpdateAccount looks up the account by id and overwrites the token without
        // reading the old one, so the garbage ciphertext does not block the repair.
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert — the credential is repaired and returned with TokenStatus "present".
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        CredentialUpdateResult? result = await response.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Credential.TokenStatus.ShouldBe("present");
    }

    [Fact]
    public async Task WhenFreshTokenSupplied_GetAccountsReturnsPresentAfterRepair()
    {
        // Arrange — create a real account via POST, then corrupt only its token column.
        Guid accountId = await SeedAccountWithCorruptedTokenAsync();

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = FreshToken,
        };

        HttpResponseMessage updateResponse = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Act — reload through GET /api/accounts to confirm the persisted state.
        HttpResponseMessage getResponse = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — the reloaded credential shows "present", not "unreadable".
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<CredentialSummary>? accounts = await getResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<CredentialSummary>>(TestContext.Current.CancellationToken);
        accounts.ShouldNotBeNull();
        CredentialSummary account = accounts.ShouldHaveSingleItem();
        account.TokenStatus.ShouldBe("present");
    }

    private sealed class ReturnsAlwaysOkStub(ValidateToken.Response response)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<ValidateToken.Response>.Ok(response));
    }
}
