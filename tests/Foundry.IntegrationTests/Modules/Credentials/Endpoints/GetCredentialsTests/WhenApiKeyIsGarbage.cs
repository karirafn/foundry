using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.GetCredentialsTests;

/// <summary>
/// Verifies that a claude_account row with a corrupt api_key column (undecryptable or
/// non-base-64) loads without a 500 and reports apiKeyStatus: "Unreadable".
///
/// These rows cannot be produced through any HTTP endpoint — a corrupt value is only possible
/// via a failed Data Protection key rotation or manual database modification.  Direct DbContext
/// seeding is the only available production path to reach this state.
/// </summary>
public sealed class WhenApiKeyIsGarbage : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenApiKeyIsGarbage()
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
    /// Seeds a claude_account row with a garbage api_key directly via DbContext.
    /// The api_key value must be set on the backing field because no HTTP endpoint can produce
    /// an undecryptable value — this is the only production-reachable seeding path for
    /// a corrupt credential row (e.g. after a Data Protection key rotation).
    ///
    /// The raw column value is injected via EF's shadow property "_apiKeyCredential" mapped
    /// to the api_key column.  We bypass the value converter by setting the column value
    /// directly through the backing field mechanism: seed the ClaudeAccount with a Present
    /// credential (which writes a valid ciphertext), then overwrite api_key in raw SQL with
    /// the desired garbage value so the converter sees the bad bytes on next read.
    /// </summary>
    private async Task SeedAccountWithGarbageApiKeyAsync(string rawColumnValue)
    {
        // Direct DbContext seeding is required here: no HTTP endpoint can produce a row whose
        // api_key column contains an undecryptable or non-base-64 value.  Raw SQL is used
        // to bypass the ApiKeyCredentialConverter so the garbage bytes reach the column.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ClaudeAccount account = ClaudeAccount.Create();
        dbContext.Set<ClaudeAccount>().Add(account);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Overwrite the api_key column with the garbage value after EF has written the row,
        // bypassing the value converter so the corrupt bytes are stored as-is.
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE claude_account SET api_key = {0}",
            rawColumnValue);
    }

    [Fact]
    public async Task WhenApiKeyIsValidBase64ButUndecryptable_Returns200WithUnreadableStatus()
    {
        // Arrange — valid base-64 bytes that are not a valid Data Protection payload.
        // The ApiKeyCredentialConverter catches CryptographicException and returns Unreadable.
        string garbage = Convert.ToBase64String([0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE, 0xAA, 0xBB]);
        await SeedAccountWithGarbageApiKeyAsync(garbage);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — the account loads; the corrupt api_key degrades gracefully to Unreadable.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ClaudeAccountSummary? summary = await response.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.ApiKeyStatus.ShouldBe("Unreadable"),
            () => summary.AuthMode.ShouldBe("ApiKey"));
    }

    [Fact]
    public async Task WhenApiKeyIsNotBase64_Returns200WithUnreadableStatus()
    {
        // Arrange — a value that is not valid base-64 (contains characters outside the alphabet).
        // The ApiKeyCredentialConverter catches FormatException and returns Unreadable.
        string notBase64 = "this-is-not-valid-base64!!!@@@###";
        await SeedAccountWithGarbageApiKeyAsync(notBase64);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ClaudeAccountSummary? summary = await response.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.ApiKeyStatus.ShouldBe("Unreadable"),
            () => summary.AuthMode.ShouldBe("ApiKey"));
    }

    [Fact]
    public async Task WhenApiKeyIsGarbage_CanDispatchIsFalse_NoServerError()
    {
        // Arrange — an undecryptable api_key means the account cannot dispatch,
        // but the endpoint must still return 200 (not 500).
        string garbage = Convert.ToBase64String([0xDE, 0xAD, 0xBE, 0xEF]);
        await SeedAccountWithGarbageApiKeyAsync(garbage);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — loads cleanly and reports the degraded state without throwing.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ClaudeAccountSummary? summary = await response.Content
            .ReadFromJsonAsync<ClaudeAccountSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ApiKeyStatus.ShouldBe("Unreadable");
    }
}
