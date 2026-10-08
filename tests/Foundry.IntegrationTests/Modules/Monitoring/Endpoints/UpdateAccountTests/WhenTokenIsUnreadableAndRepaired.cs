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
/// Verifies that a credential whose token column contains garbage ciphertext
/// (unreadable by the active key ring) can be repaired by supplying a fresh
/// valid token via PUT /api/accounts/{id} (AC4 from issue #555).
/// </summary>
public sealed class WhenTokenIsUnreadableAndRepaired : IAsyncDisposable
{
    private const string AccountsTable = "accounts";

    // Valid base64 that the ephemeral key ring cannot unprotect.
    private const string GarbageBase64Token =
        "AAAA/totally/random+bytes+that+no+key+ring+can+ever+decrypt==";

    private const string FreshToken = "ghp_fresh_valid_token";
    private const string RepairedAccountName = "repaired-user";
    private const string AccountName = "unreadable-account";

    // One writable repo under "repaired-user" so namespace derivation passes during repair.
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
            // Stub ValidateToken so the fresh token resolves successfully without a real provider call.
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
    /// Inserts a credentials row with garbage ciphertext directly via raw SQL,
    /// bypassing the ProviderTokenConverter write leg that rejects Unreadable tokens.
    /// </summary>
    private async Task<Guid> SeedRowWithGarbageTokenAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Ensure migrations have run so the table exists.
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        Guid id = Guid.NewGuid();

        // type = "github" is the TPH discriminator for GitHubCredential.
        // base_url must include the trailing slash; host is the bare domain.
        await dbContext.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {AccountsTable} (id, name, token, base_url, host, type) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
            id.ToString(),
            AccountName,
            GarbageBase64Token,
            "https://github.com/",
            "github.com",
            "github");

        return id;
    }

    [Fact]
    public async Task WhenFreshTokenSupplied_UpdateAccountReturnsPresent()
    {
        // Arrange — seed a row whose token column contains garbage ciphertext so
        // ProviderToken materializes as Unreadable on load.
        Guid accountId = await SeedRowWithGarbageTokenAsync();

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = FreshToken,
        };

        // Act — supply a fresh valid token via PUT /api/accounts/{id}.
        // UpdateAccount does not read the old token; it writes the new one directly.
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
        // Arrange — seed a garbage-ciphertext row, then repair it via PUT.
        Guid accountId = await SeedRowWithGarbageTokenAsync();

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
