using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.UpdateAuthModeTests;

/// <summary>
/// Verifies the key re-entry repair path: when a claude_account has an Unreadable api_key
/// (e.g. after a Data Protection key rotation), submitting a new API key via PUT /api/credentials/auth
/// stores the new key and transitions apiKeyStatus to "Present", restoring dispatch eligibility.
/// </summary>
public sealed class WhenApiKeyIsUnreadable : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenApiKeyIsUnreadable()
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
    /// Direct DbContext seeding is required here: no HTTP endpoint can produce an undecryptable
    /// api_key — this state arises only from a Data Protection key rotation or manual modification.
    /// </summary>
    private async Task SeedAccountWithUnreadableApiKeyAsync()
    {
        // Direct DbContext seeding is required here: no HTTP endpoint can produce a row whose
        // api_key column contains an undecryptable value.  Raw SQL is used to bypass the
        // ApiKeyCredentialConverter and write the garbage bytes directly to the column.
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
    public async Task WhenNewApiKeySubmitted_ReturnsOkWithPresentStatus()
    {
        // Arrange — account starts with an Unreadable api_key.
        await SeedAccountWithUnreadableApiKeyAsync();
        object body = new { mode = "api_key", apiKey = "sk-ant-repaired-key-xyz987" };

        // Act — re-enter the API key via the repair endpoint.
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/credentials/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert — the new key is stored and apiKeyStatus transitions to Present.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ClaudeAccountSummary? summary = await response.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("ApiKey"),
            () => summary.ApiKeyStatus.ShouldBe("Present"));
    }

    [Fact]
    public async Task WhenNewApiKeySubmitted_SubsequentLoadShowsPresentStatus()
    {
        // Arrange — account starts with an Unreadable api_key.
        await SeedAccountWithUnreadableApiKeyAsync();
        object body = new { mode = "api_key", apiKey = "sk-ant-repair-then-load-key-abc" };

        // Act — repair via PUT, then reload via GET.
        await _client.PutAsJsonAsync(
            new Uri("/api/credentials/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        HttpResponseMessage getResponse = await _client.GetAsync(
            new Uri("/api/credentials", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — subsequent load also reports Present (the new key decrypts successfully).
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        ClaudeAccountSummary? summary = await getResponse.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ApiKeyStatus.ShouldBe("Present");
    }
}
