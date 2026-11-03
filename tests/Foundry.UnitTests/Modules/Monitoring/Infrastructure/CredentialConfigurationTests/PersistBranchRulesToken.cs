using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using ProviderToken = Foundry.Modules.Monitoring.Domain.ValueObjects.ProviderToken;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.CredentialConfigurationTests;

public sealed class PersistBranchRulesToken : IAsyncDisposable
{
    private const string RulesToken = "github_pat_rules_secret";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistBranchRulesToken()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        IDataProtectionProvider dataProtectionProvider = DataProtectionProvider.Create("Foundry.Test");

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options, dataProtectionProvider);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<Credential> SaveAndReloadAsync(Credential credential)
    {
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        Credential? reloaded = await _dbContext
            .Set<Credential>()
            .FindAsync([credential.Id], TestContext.Current.CancellationToken);
        return reloaded.ShouldNotBeNull();
    }

    private async Task<string?> ReadRawColumnAsync()
    {
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT branch_rules_token FROM accounts";
        object? value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is DBNull ? null : (string?)value;
    }

    [Fact]
    public async Task WhenPersistedWithBranchRulesToken_RoundTripsAsPresent()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(
            "my-org", "ghp_worker", BaseUrl.Create("https://github.com").ValueOrThrow(), RulesToken);

        // Act
        Credential reloaded = await SaveAndReloadAsync(credential);

        // Assert
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.BranchRulesToken.ShouldBe(new ProviderToken.Present(RulesToken)),
            () => reloaded.Token.ShouldBe(new ProviderToken.Present("ghp_worker")));
    }

    [Fact]
    public async Task WhenPersistedWithBranchRulesToken_StoresCiphertextNotPlaintext()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(
            "my-org", "ghp_worker", BaseUrl.Create("https://github.com").ValueOrThrow(), RulesToken);

        // Act
        await SaveAndReloadAsync(credential);
        string? raw = await ReadRawColumnAsync();

        // Assert
        raw.ShouldNotBeNull();
        raw.ShouldNotContain(RulesToken);
    }

    [Fact]
    public async Task WhenPersistedWithoutBranchRulesToken_ColumnIsNullAndReloadsAbsent()
    {
        // Arrange
        GitLabCredential credential = GitLabCredential.Create(
            "my-group", "glpat-worker", BaseUrl.Create("https://gitlab.com").ValueOrThrow());

        // Act
        Credential reloaded = await SaveAndReloadAsync(credential);

        // Assert
        reloaded.BranchRulesToken.ShouldBeNull();
        (await ReadRawColumnAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task WhenStoredCiphertextIsGarbage_ReloadsAsUnreadableWithoutAffectingAccountToken()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(
            "my-org", "ghp_worker", BaseUrl.Create("https://github.com").ValueOrThrow(), RulesToken);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE accounts SET branch_rules_token = {0}",
            ["AAAA/totally/random+bytes+that+no+key+ring+can+ever+decrypt=="],
            TestContext.Current.CancellationToken);

        // Act
        Credential? reloaded = await _dbContext
            .Set<Credential>()
            .FindAsync([credential.Id], TestContext.Current.CancellationToken);

        // Assert
        reloaded.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.IsBranchRulesTokenUnreadable.ShouldBeTrue(),
            () => reloaded.IsTokenUnreadable.ShouldBeFalse(),
            () => reloaded.Token.ShouldBe(new ProviderToken.Present("ghp_worker")));
    }
}
