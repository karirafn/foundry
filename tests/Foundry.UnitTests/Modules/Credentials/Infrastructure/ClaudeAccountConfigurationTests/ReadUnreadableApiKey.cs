using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Infrastructure.ClaudeAccountConfigurationTests;

/// <summary>
/// Tests that the api_key column can degrade gracefully to <see cref="ApiKeyCredential.Unreadable"/>
/// or <see cref="ApiKeyCredential.NotConfigured"/> instead of throwing during EF materialization.
/// </summary>
public sealed class ReadUnreadableApiKey : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public ReadUnreadableApiKey()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        IDataProtectionProvider provider = DataProtectionProvider.Create("TestApp");
        _dbContext = new FoundryDbContext(options, provider);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenApiKeyColumnHasUndecryptableValue_LoadsAsUnreadable()
    {
        // Arrange — insert a row with a valid ciphertext from a different key ring
        ClaudeAccount account = ClaudeAccount.Create();
        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Overwrite the api_key column with ciphertext produced by a *different* key ring
        // so that decryption with the test key ring fails with CryptographicException.
        IDataProtectionProvider differentProvider = DataProtectionProvider.Create("DifferentApp");
        IDataProtector differentProtector = differentProvider
            .CreateProtector("Foundry.Settings.Encryption");
        byte[] garbage = differentProtector.Protect(System.Text.Encoding.UTF8.GetBytes("some-key"));
        string undecryptable = Convert.ToBase64String(garbage);

        await using SqliteCommand update = _connection.CreateCommand();
        update.CommandText = "UPDATE claude_account SET api_key = @v";
        update.Parameters.AddWithValue("@v", undecryptable);
        await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        // Act
        ClaudeAccount? result = await _dbContext
            .Set<ClaudeAccount>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount reloaded = result.ShouldNotBeNull();
        AuthMode.ApiKey apiKey = reloaded.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
        apiKey.Credential.ShouldBeOfType<ApiKeyCredential.Unreadable>();
    }

    [Fact]
    public async Task WhenApiKeyColumnHasInvalidBase64_LoadsAsUnreadable()
    {
        // Arrange — insert a valid row then overwrite api_key with non-base64 garbage
        ClaudeAccount account = ClaudeAccount.Create();
        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        await using SqliteCommand update = _connection.CreateCommand();
        update.CommandText = "UPDATE claude_account SET api_key = @v";
        update.Parameters.AddWithValue("@v", "not-valid-base64!!!");
        await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        // Act
        ClaudeAccount? result = await _dbContext
            .Set<ClaudeAccount>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount reloaded = result.ShouldNotBeNull();
        AuthMode.ApiKey apiKey = reloaded.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
        apiKey.Credential.ShouldBeOfType<ApiKeyCredential.Unreadable>();
    }

    [Fact]
    public async Task WhenApiKeyColumnIsNull_InApiKeyMode_LoadsAsNotConfigured()
    {
        // Arrange — Create produces an account in API-key mode with NotConfigured credential;
        // the api_key column will be NULL.
        ClaudeAccount account = ClaudeAccount.Create();
        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        ClaudeAccount? result = await _dbContext
            .Set<ClaudeAccount>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount reloaded = result.ShouldNotBeNull();
        AuthMode.ApiKey apiKey = reloaded.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
        apiKey.Credential.ShouldBeOfType<ApiKeyCredential.NotConfigured>();
    }

    [Fact]
    public async Task WhenOAuthRowHasNullApiKey_LoadsOAuthModeAndAuthModeIsReadable()
    {
        // Arrange — an OAuth row has no api_key (NULL) by design; auth_mode is plaintext JSON.
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        ClaudeAccount? result = await _dbContext
            .Set<ClaudeAccount>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        ClaudeAccount reloaded = result.ShouldNotBeNull();
        AuthMode.OAuth oauthMode = reloaded.AuthMode.ShouldBeOfType<AuthMode.OAuth>();
        oauthMode.SubscriptionType.ShouldBe("pro");
    }

    [Fact]
    public async Task WhenAuthModeColumnHasLegacyEncryptedBlob_LoadsAsApiKeyNotConfigured()
    {
        // Arrange — simulate a legacy row where auth_mode contains old-format encrypted ciphertext
        // rather than plaintext JSON (the pre-step-3 single-column scheme).
        ClaudeAccount account = ClaudeAccount.Create();
        _dbContext.Set<ClaudeAccount>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Overwrite auth_mode with content that is not valid JSON (simulates old ciphertext).
        await using SqliteCommand update = _connection.CreateCommand();
        update.CommandText = "UPDATE claude_account SET auth_mode = @v";
        update.Parameters.AddWithValue("@v", "CipherTextThatIsNotValidJson==");
        await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        // Act
        ClaudeAccount? result = await _dbContext
            .Set<ClaudeAccount>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert — account loads with a recoverable state rather than throwing
        ClaudeAccount reloaded = result.ShouldNotBeNull();
        AuthMode.ApiKey apiKey = reloaded.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
        apiKey.Credential.ShouldBeOfType<ApiKeyCredential.NotConfigured>();
    }
}
