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
/// Endpoint-level coverage of the optional branch-rules token: it is accepted on
/// create/update, persisted encrypted, never echoed by any accounts endpoint (only its status),
/// reads <c>unreadable</c> when its ciphertext cannot be decrypted, and is repaired or cleared
/// through PUT /api/accounts/{id}.
/// </summary>
public sealed class WhenBranchRulesTokenIsManaged : IAsyncDisposable
{
    private static readonly System.Text.Json.JsonSerializerOptions WebJson =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private const string GarbageBase64Token =
        "AAAA/totally/random+bytes+that+no+key+ring+can+ever+decrypt==";

    private const string RulesToken = "github_pat_branch_rules_secret";
    private const string FreshRulesToken = "github_pat_branch_rules_fresh";
    private const string RepairedAccountName = "repaired-user";

    private const string RepairedUserListingJson =
        """[{"full_name":"repaired-user/repo","private":false,"permissions":{"push":true}}]""";

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenBranchRulesTokenIsManaged()
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

    private async Task<(Guid Id, string RawBody)> CreateAccountAsync(string? branchRulesToken)
    {
        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_placeholder_token",
            branchRulesToken,
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        string raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        CredentialCreationResult? created = System.Text.Json.JsonSerializer.Deserialize<CredentialCreationResult>(
            raw,
            WebJson);
        created.ShouldNotBeNull();
        return (created.Credential.Id, raw);
    }

    private async Task<(CredentialSummary Account, string RawBody)> GetSingleAccountAsync()
    {
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<CredentialSummary>? accounts = System.Text.Json.JsonSerializer.Deserialize<IReadOnlyList<CredentialSummary>>(
            raw,
            WebJson);
        accounts.ShouldNotBeNull();
        return (accounts.ShouldHaveSingleItem(), raw);
    }

    private async Task<CredentialUpdateResult> PutAsync(Guid accountId, object body)
    {
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        CredentialUpdateResult? result = await response.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(TestContext.Current.CancellationToken);
        return result.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenCreatedWithBranchRulesToken_ReportsPresentAndNeverReturnsTheToken()
    {
        // Arrange / Act
        (Guid _, string createRaw) = await CreateAccountAsync(RulesToken);
        (CredentialSummary account, string getRaw) = await GetSingleAccountAsync();

        // Assert
        account.BranchRulesTokenStatus.ShouldBe("present");
        createRaw.ShouldNotContain(RulesToken);
        getRaw.ShouldNotContain(RulesToken);
    }

    [Fact]
    public async Task WhenCreatedWithBranchRulesToken_StoresCiphertext()
    {
        // Arrange / Act
        await CreateAccountAsync(RulesToken);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        List<string?> stored = await dbContext.Database
            .SqlQueryRaw<string?>("SELECT branch_rules_token AS Value FROM accounts")
            .ToListAsync(TestContext.Current.CancellationToken);
        string? raw = stored.ShouldHaveSingleItem();
        raw.ShouldNotBeNull();
        raw.ShouldNotContain(RulesToken);
    }

    [Fact]
    public async Task WhenCreatedWithoutBranchRulesToken_ReportsAbsent()
    {
        // Arrange / Act
        await CreateAccountAsync(branchRulesToken: null);
        (CredentialSummary account, string _) = await GetSingleAccountAsync();

        // Assert
        account.ShouldSatisfyAllConditions(
            () => account.BranchRulesTokenStatus.ShouldBe("absent"),
            () => account.TokenStatus.ShouldBe("present"));
    }

    [Fact]
    public async Task WhenStoredBranchRulesTokenIsGarbage_ReadsUnreadableAndIsRepairedViaUpdate()
    {
        // Arrange — corrupt only the branch-rules column (one row in this per-class database).
        Guid accountId = (await CreateAccountAsync(RulesToken)).Id;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE accounts SET branch_rules_token = {0}",
                [GarbageBase64Token],
                TestContext.Current.CancellationToken);
        }

        (CredentialSummary unreadable, string _) = await GetSingleAccountAsync();
        unreadable.ShouldSatisfyAllConditions(
            () => unreadable.BranchRulesTokenStatus.ShouldBe("unreadable"),
            () => unreadable.TokenStatus.ShouldBe("present"));

        // Act
        CredentialUpdateResult result = await PutAsync(accountId, new
        {
            baseUrl = "https://github.com",
            token = (string?)null,
            branchRulesToken = FreshRulesToken,
        });

        // Assert
        result.Credential.BranchRulesTokenStatus.ShouldBe("present");
        (CredentialSummary repaired, string raw) = await GetSingleAccountAsync();
        repaired.BranchRulesTokenStatus.ShouldBe("present");
        raw.ShouldNotContain(FreshRulesToken);
    }

    [Fact]
    public async Task WhenClearRequested_RemovesBranchRulesToken()
    {
        // Arrange
        Guid accountId = (await CreateAccountAsync(RulesToken)).Id;

        // Act
        CredentialUpdateResult result = await PutAsync(accountId, new
        {
            baseUrl = "https://github.com",
            token = (string?)null,
            clearBranchRulesToken = true,
        });

        // Assert
        result.Credential.BranchRulesTokenStatus.ShouldBe("absent");
        (CredentialSummary account, string _) = await GetSingleAccountAsync();
        account.BranchRulesTokenStatus.ShouldBe("absent");
    }

    [Fact]
    public async Task WhenBothSetAndClearRequested_ReturnsBadRequest()
    {
        // Arrange
        Guid accountId = (await CreateAccountAsync(RulesToken)).Id;

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            new { baseUrl = "https://github.com", token = (string?)null, branchRulesToken = FreshRulesToken, clearBranchRulesToken = true },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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
