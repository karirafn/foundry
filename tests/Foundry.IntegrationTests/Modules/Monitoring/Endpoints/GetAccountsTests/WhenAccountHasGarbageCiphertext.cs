using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetAccountsTests;

/// <summary>
/// Verifies that a credential row whose token column contains garbage ciphertext
/// (bytes that cannot be decrypted by the active key ring) is exposed by
/// GET /api/accounts with TokenStatus = "unreadable".
///
/// The garbage ciphertext is written directly via raw SQL because the
/// ProviderTokenConverter write leg throws on Unreadable and cannot produce it,
/// and a token encrypted by the factory's ephemeral key ring would decrypt normally.
/// </summary>
public sealed class WhenAccountHasGarbageCiphertext : IAsyncDisposable
{
    // Table name from CredentialConfiguration.
    private const string AccountsTable = "accounts";

    // Valid base64 that decodes to bytes no key ring can unprotect.
    // The bytes are not a valid DPAPI/EphemeralDataProtection payload,
    // so IDataProtector.Unprotect throws CryptographicException.
    private const string GarbageBase64Token =
        "AAAA/totally/random+bytes+that+no+key+ring+can+ever+decrypt==";

    // A string that is not valid base64 at all.
    // Convert.FromBase64String throws FormatException on this value.
    private const string NonBase64Token = "not-valid-base64!!!";

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountHasGarbageCiphertext()
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
    /// Inserts a credentials row directly via ExecuteSqlRawAsync, bypassing the
    /// ProviderTokenConverter write leg (which rejects Unreadable tokens).
    /// The TPH discriminator ("github") and all NOT-NULL columns are provided.
    /// </summary>
    private async Task<Guid> SeedRowWithGarbageTokenAsync(string accountName, string rawTokenValue)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Ensure migrations have run so the table exists.
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        Guid id = Guid.NewGuid();

        // type = "github" matches CredentialDiscriminators.GitHub (TPH discriminator).
        // base_url must include the trailing slash (BaseUrl stores it with a trailing slash).
        await dbContext.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {AccountsTable} (id, name, token, base_url, host, type) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
            id.ToString(),
            accountName,
            rawTokenValue,
            "https://github.com/",
            "github.com",
            "github");

        return id;
    }

    [Fact]
    public async Task WhenTokenIsValidBase64ButUndecryptable_ReturnsUnreadableStatus()
    {
        // Arrange — insert a row whose token is valid base64 but cannot be decrypted
        // by the ephemeral key ring. IDataProtector.Unprotect throws CryptographicException,
        // and ProviderTokenConverter maps that to ProviderToken.Unreadable.
        await SeedRowWithGarbageTokenAsync("garbage-base64-account", GarbageBase64Token);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<CredentialSummary>? accounts = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<CredentialSummary>>(TestContext.Current.CancellationToken);
        accounts.ShouldNotBeNull();
        CredentialSummary account = accounts.ShouldHaveSingleItem();
        account.TokenStatus.ShouldBe("unreadable");
    }

    [Fact]
    public async Task WhenTokenIsNotValidBase64_ReturnsUnreadableStatus()
    {
        // Arrange — insert a row whose token column is not valid base64 at all.
        // Convert.FromBase64String throws FormatException, which ProviderTokenConverter
        // also maps to ProviderToken.Unreadable.
        await SeedRowWithGarbageTokenAsync("non-base64-account", NonBase64Token);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<CredentialSummary>? accounts = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<CredentialSummary>>(TestContext.Current.CancellationToken);
        accounts.ShouldNotBeNull();
        CredentialSummary account = accounts.ShouldHaveSingleItem();
        account.TokenStatus.ShouldBe("unreadable");
    }

    [Fact]
    public async Task WhenTokenIsValidBase64ButUndecryptable_HasTokenIsTrue()
    {
        // Arrange — same garbage row; verify HasToken is true (the column is non-null)
        // even though the token cannot be decrypted.
        await SeedRowWithGarbageTokenAsync("has-token-account", GarbageBase64Token);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<CredentialSummary>? accounts = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<CredentialSummary>>(TestContext.Current.CancellationToken);
        accounts.ShouldNotBeNull();
        CredentialSummary account = accounts.ShouldHaveSingleItem();
        account.ShouldSatisfyAllConditions(
            () => account.HasToken.ShouldBeTrue(),
            () => account.TokenStatus.ShouldBe("unreadable"));
    }
}
