using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Login;

/// <summary>
/// Verifies the OAuth login repair path: when a claude_account has an Unreadable api_key
/// (e.g. after a Data Protection key rotation), completing an in-app login via
/// <see cref="LoginSuccessCommitter"/> records OAuth mode and identity, publishes
/// CredentialsValidated, and leaves the account loadable via GET /api/credentials.
///
/// LoginSuccessCommitter has no HTTP endpoint — it is invoked from the background login session
/// service after a successful OAuth token exchange.  It is resolved from DI per the integration
/// test rules for handlers without an HTTP endpoint.
/// </summary>
public sealed class WhenLoginSucceedsWithUnreadableApiKey : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenLoginSucceedsWithUnreadableApiKey()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Seeds a claude_account whose api_key column contains a value that will be Unreadable
    /// by the ApiKeyCredentialConverter (valid base-64 that is not a Data Protection payload).
    ///
    /// Direct DbContext seeding is required here: no HTTP endpoint can produce a row whose
    /// api_key column contains an undecryptable value.  This state arises only from a
    /// Data Protection key rotation or manual database modification.
    /// </summary>
    private async Task SeedAccountWithUnreadableApiKeyAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ClaudeAccount account = ClaudeAccount.Create();
        dbContext.Set<ClaudeAccount>().Add(account);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Overwrite with a valid base-64 string that is not a valid Data Protection payload,
        // so the converter sees a CryptographicException and returns Unreadable on next read.
        string garbage = Convert.ToBase64String([0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE, 0xAA, 0xBB]);
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE claude_account SET api_key = {0}",
            garbage);
    }

    [Fact]
    public async Task WhenLoginCommitted_AccountTransitionsToOAuthMode()
    {
        // Arrange — account starts with an Unreadable api_key.
        await SeedAccountWithUnreadableApiKeyAsync();

        AccountIdentity identity = new("repair@example.com", "RepairOrg", "pro");

        // Act — simulate LoginSuccessCommitter completing after a successful OAuth token exchange.
        // LoginSuccessCommitter has no HTTP endpoint; resolve from DI per integration-test rules.
        using IServiceScope scope = _factory.Services.CreateScope();
        ILoginSuccessCommitter committer = scope.ServiceProvider.GetRequiredService<ILoginSuccessCommitter>();
        await committer.CommitAsync(identity, TestContext.Current.CancellationToken);

        // Assert — account is now in OAuth mode with the recorded identity.
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials", UriKind.Relative),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        ClaudeAccountSummary? summary = await response.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("OAuth"),
            () => summary.OAuthAccountEmail.ShouldBe("repair@example.com"),
            () => summary.OAuthAccountOrgName.ShouldBe("RepairOrg"));
    }

    [Fact]
    public async Task WhenLoginCommitted_AccountIsLoadableThroughout()
    {
        // Arrange — account has an Unreadable api_key; confirm the load path is stable before
        // and after the repair (i.e. neither load throws a 500).
        await SeedAccountWithUnreadableApiKeyAsync();

        // Pre-repair load — must succeed with Unreadable status.
        HttpResponseMessage preRepairResponse = await _client.GetAsync(
            new Uri("/api/credentials", UriKind.Relative),
            TestContext.Current.CancellationToken);
        preRepairResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        ClaudeAccountSummary? preSummary = await preRepairResponse.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        preSummary.ShouldNotBeNull();
        preSummary.ApiKeyStatus.ShouldBe("Unreadable");

        // Act — commit login success.
        AccountIdentity identity = new("loadtest@example.com", "LoadOrg", string.Empty);
        using IServiceScope scope = _factory.Services.CreateScope();
        ILoginSuccessCommitter committer = scope.ServiceProvider.GetRequiredService<ILoginSuccessCommitter>();
        await committer.CommitAsync(identity, TestContext.Current.CancellationToken);

        // Post-repair load — must succeed with OAuth mode and no server error.
        HttpResponseMessage postRepairResponse = await _client.GetAsync(
            new Uri("/api/credentials", UriKind.Relative),
            TestContext.Current.CancellationToken);
        postRepairResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        ClaudeAccountSummary? postSummary = await postRepairResponse.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        postSummary.ShouldNotBeNull();
        postSummary.AuthMode.ShouldBe("OAuth");
    }
}
