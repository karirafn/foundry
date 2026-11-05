using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared.Infrastructure.DataProtection;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DataProtection.DataProtectedColumnReEncryptorTests;

public sealed class ReEncryptAsync : IAsyncDisposable
{
    private const string ApiKeyPurpose = "Foundry.Settings.Encryption";
    private const string UndecryptablePlaintext = "unrelated-secret";

    private readonly LegacyEncryptedDatabase _database = new();
    private readonly CapturingLogger<DataProtectedColumnReEncryptor> _logger = new();

    async ValueTask IAsyncDisposable.DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task LegacyCiphertext_IsRewrittenSoTheCurrentProviderReadsTheOriginalValues()
    {
        // Arrange
        await _database.SeedAsync(_database.Legacy, TestContext.Current.CancellationToken);

        // Act
        await ReEncryptAsyncUnderTest();

        // Assert
        await using FoundryDbContext reader = _database.CreateDbContext(_database.Current);
        GitHubCredential credential = await reader.Set<GitHubCredential>().SingleAsync(TestContext.Current.CancellationToken);
        MonitoredRepository repository = await reader.Set<MonitoredRepository>().SingleAsync(TestContext.Current.CancellationToken);
        ClaudeAccount account = await reader.Set<ClaudeAccount>().SingleAsync(TestContext.Current.CancellationToken);
        credential.Token.ShouldBe(new ProviderToken.Present(LegacyEncryptedDatabase.TokenValue));
        credential.BranchRulesToken.ShouldBe(new ProviderToken.Present(LegacyEncryptedDatabase.BranchRulesTokenValue));
        repository.EnsureWebhookSecret().ShouldBe(_database.WebhookSecret);
        account.AuthMode.ShouldBe(new AuthMode.ApiKey(new ApiKeyCredential.Present(LegacyEncryptedDatabase.ApiKeyValue)));
    }

    [Fact]
    public async Task ValuesAlreadyUnderTheCurrentDiscriminator_StayByteIdentical()
    {
        // Arrange
        await _database.SeedAsync(_database.Current, TestContext.Current.CancellationToken);
        Dictionary<string, string?> before = await _database.ReadRawAsync(TestContext.Current.CancellationToken);

        // Act
        await ReEncryptAsyncUnderTest();

        // Assert
        Dictionary<string, string?> after = await _database.ReadRawAsync(TestContext.Current.CancellationToken);
        after.ShouldBe(before);
    }

    [Fact]
    public async Task SecondRun_ChangesNothing()
    {
        // Arrange
        await _database.SeedAsync(_database.Legacy, TestContext.Current.CancellationToken);
        await ReEncryptAsyncUnderTest();
        Dictionary<string, string?> afterFirstRun = await _database.ReadRawAsync(TestContext.Current.CancellationToken);

        // Act
        await ReEncryptAsyncUnderTest();

        // Assert
        Dictionary<string, string?> afterSecondRun = await _database.ReadRawAsync(TestContext.Current.CancellationToken);
        afterSecondRun.ShouldBe(afterFirstRun);
    }

    [Fact]
    public async Task EmptyStringColumn_IsSkippedWithoutWarning()
    {
        // Arrange
        await _database.SeedAsync(_database.Legacy, TestContext.Current.CancellationToken);
        await _database.SetApiKeyColumnAsync(string.Empty, TestContext.Current.CancellationToken);

        // Act
        await ReEncryptAsyncUnderTest();

        // Assert
        Dictionary<string, string?> raw = await _database.ReadRawAsync(TestContext.Current.CancellationToken);
        raw.Where(entry => entry.Key.StartsWith("claude_account.api_key.", StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .ShouldHaveSingleItem()
            .ShouldBe(string.Empty);
        _logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task NullColumn_IsSkippedWithoutWarning()
    {
        // Arrange
        await _database.SeedAsync(_database.Legacy, TestContext.Current.CancellationToken);
        await _database.SetApiKeyColumnAsync(null, TestContext.Current.CancellationToken);

        // Act
        await ReEncryptAsyncUnderTest();

        // Assert
        Dictionary<string, string?> raw = await _database.ReadRawAsync(TestContext.Current.CancellationToken);
        raw.Where(entry => entry.Key.StartsWith("claude_account.api_key.", StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .ShouldHaveSingleItem()
            .ShouldBeNull();
        _logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task DatabaseWithoutEncryptedRows_CompletesWithoutLogging()
    {
        // Arrange — nothing seeded

        // Act
        await ReEncryptAsyncUnderTest();

        // Assert
        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task UndecryptableValue_StaysUnchangedAndOtherRowsAreStillRewritten()
    {
        // Arrange
        await _database.SeedAsync(_database.Legacy, TestContext.Current.CancellationToken);
        string undecryptable = DataProtectedText.Protect(
            _database.Unrelated.CreateProtector(ApiKeyPurpose),
            UndecryptablePlaintext);
        await _database.SetApiKeyColumnAsync(undecryptable, TestContext.Current.CancellationToken);

        // Act
        await ReEncryptAsyncUnderTest();

        // Assert
        Dictionary<string, string?> raw = await _database.ReadRawAsync(TestContext.Current.CancellationToken);
        raw.Where(entry => entry.Key.StartsWith("claude_account.api_key.", StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .ShouldHaveSingleItem()
            .ShouldBe(undecryptable);
        await using FoundryDbContext reader = _database.CreateDbContext(_database.Current);
        GitHubCredential credential = await reader.Set<GitHubCredential>().SingleAsync(TestContext.Current.CancellationToken);
        credential.Token.ShouldBe(new ProviderToken.Present(LegacyEncryptedDatabase.TokenValue));
    }

    [Fact]
    public async Task UndecryptableValue_LogsOneWarningNamingTableColumnAndRowOnly()
    {
        // Arrange
        await _database.SeedAsync(_database.Legacy, TestContext.Current.CancellationToken);
        string undecryptable = DataProtectedText.Protect(
            _database.Unrelated.CreateProtector(ApiKeyPurpose),
            UndecryptablePlaintext);
        await _database.SetApiKeyColumnAsync(undecryptable, TestContext.Current.CancellationToken);
        Dictionary<string, string?> before = await _database.ReadRawAsync(TestContext.Current.CancellationToken);
        string rowId = before.Keys.Single(key => key.StartsWith("claude_account.api_key.", StringComparison.Ordinal))["claude_account.api_key.".Length..];

        // Act
        await ReEncryptAsyncUnderTest();

        // Assert
        string warning = _logger.Entries
            .Where(entry => entry.Level == LogLevel.Warning)
            .ShouldHaveSingleItem()
            .Message;
        warning.ShouldContain("claude_account");
        warning.ShouldContain("api_key");
        warning.ShouldContain(rowId);
        warning.ShouldNotContain(undecryptable);
        warning.ShouldNotContain(UndecryptablePlaintext);
    }

    private async Task ReEncryptAsyncUnderTest()
    {
        await using FoundryDbContext dbContext = _database.CreateDbContext(_database.Current);
        DataProtectedColumnReEncryptor reEncryptor = new(_database.Current, _database.Legacy, _logger);

        await reEncryptor.ReEncryptAsync(dbContext, TestContext.Current.CancellationToken);
    }
}
