using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.AccountTokenChecks;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.AccountTokenChecks.AccountTokenExpiryBackfillServiceTests;

/// <summary>
/// <see cref="AccountTokenExpiryBackfillService"/> reads the expiry once for each account saved before expiry
/// was tracked, and stores it only from a check that authenticated.
/// </summary>
public sealed class BackfillAsync : IAsyncDisposable
{
    private static readonly TokenExpiry ExpiresOn = new TokenExpiry.On(new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero));
    private static readonly DateTimeOffset DetectedAt = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly Error TransportFailed =
        new(AccountTokenValidator.TransportFailedCode, "No such host is known.");

    private readonly SqliteConnection _connection;
    private readonly ScriptedTokenValidator _validator = new();

    public BackfillAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenThreeAccountsAreNotChecked_EachGetsOneRequestAndStoresItsExpiry()
    {
        // Arrange
        CredentialId onAccount = await SeedAccountAsync("on-org", "ghp_on");
        CredentialId neverAccount = await SeedAccountAsync("never-org", "ghp_never");
        CredentialId unavailableAccount = await SeedAccountAsync("unavailable-org", "ghp_unavailable");
        _validator.Respond("ghp_on", TokenValidationOutcome.Authenticated("on-org", ExpiresOn));
        _validator.Respond("ghp_never", TokenValidationOutcome.Authenticated("never-org", new TokenExpiry.Never()));
        _validator.Respond("ghp_unavailable", TokenValidationOutcome.TokenOfWrongKind("unavailable-org", WrongTokenKind.GitLabResourceToken, new TokenExpiry.Unavailable()));
        AccountTokenExpiryBackfillService sut = BuildService();

        // Act
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Assert
        _validator.CallsByToken.ShouldBe(new Dictionary<string, int>
        {
            ["ghp_on"] = 1,
            ["ghp_never"] = 1,
            ["ghp_unavailable"] = 1,
        });
        (await LoadAccountAsync(onAccount)).TokenExpiry.ShouldBe(ExpiresOn);
        (await LoadAccountAsync(neverAccount)).TokenExpiry.ShouldBe(new TokenExpiry.Never());
        (await LoadAccountAsync(unavailableAccount)).TokenExpiry.ShouldBe(new TokenExpiry.Unavailable());
    }

    [Fact]
    public async Task WhenEveryAccountWasFilledIn_NextTickSendsNoRequests()
    {
        // Arrange
        await SeedAccountAsync("on-org", "ghp_on");
        await SeedAccountAsync("never-org", "ghp_never");
        _validator.Respond("ghp_on", TokenValidationOutcome.Authenticated("on-org", ExpiresOn));
        _validator.Respond("ghp_never", TokenValidationOutcome.Authenticated("never-org", new TokenExpiry.Never()));
        AccountTokenExpiryBackfillService sut = BuildService();
        await sut.BackfillAsync(TestContext.Current.CancellationToken);
        _validator.CallsByToken.Clear();

        // Act
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Assert
        _validator.CallsByToken.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenProviderIsUnreachable_AccountStaysNotChecked()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("my-org", "ghp_token");
        _validator.Respond("ghp_token", TransportFailed);
        AccountTokenExpiryBackfillService sut = BuildService();

        // Act
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenExpiry.ShouldBe(new TokenExpiry.NotChecked());
    }

    [Fact]
    public async Task WhenProviderIsReachableOnTheNextTick_AccountIsFilledIn()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("my-org", "ghp_token");
        _validator.Respond("ghp_token", TransportFailed);
        AccountTokenExpiryBackfillService sut = BuildService();
        await sut.BackfillAsync(TestContext.Current.CancellationToken);
        _validator.Respond("ghp_token", TokenValidationOutcome.Authenticated("my-org", ExpiresOn));

        // Act
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenExpiry.ShouldBe(ExpiresOn);
    }

    [Theory]
    [InlineData(nameof(TokenValidationOutcome.AuthenticationFailedOutcome))]
    [InlineData(nameof(TokenValidationOutcome.IdentityUnresolvedOutcome))]
    [InlineData(nameof(TokenValidationOutcome.ProviderMismatchOutcome))]
    public async Task WhenCheckDoesNotAuthenticate_NothingIsRecorded(string outcome)
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("my-org", "ghp_token");
        _validator.Respond("ghp_token", OutcomeWithoutAReading(outcome));
        AccountTokenExpiryBackfillService sut = BuildService();

        // Act
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Assert
        Credential stored = await LoadAccountAsync(accountId);
        stored.ShouldSatisfyAllConditions(
            () => stored.TokenExpiry.ShouldBe(new TokenExpiry.NotChecked()),
            () => stored.TokenValidity.ShouldBe(new TokenValidity.Valid()));
    }

    [Fact]
    public async Task WhenProviderReportsNoReadingYet_AccountStaysNotCheckedAndIsReadAgainNextTick()
    {
        // Arrange — a provider client that does not read the expiry yet returns NotChecked.
        CredentialId accountId = await SeedAccountAsync("my-org", "ghp_token");
        _validator.Respond("ghp_token", TokenValidationOutcome.Authenticated("my-org", new TokenExpiry.NotChecked()));
        AccountTokenExpiryBackfillService sut = BuildService();
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenExpiry.ShouldBe(new TokenExpiry.NotChecked());
        _validator.CallsByToken["ghp_token"].ShouldBe(2);
    }

    [Fact]
    public async Task WhenAccountIsInvalid_ItIsSkipped()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("my-org", "ghp_token", invalidSince: DetectedAt);
        _validator.Respond("ghp_token", TokenValidationOutcome.Authenticated("my-org", ExpiresOn));
        AccountTokenExpiryBackfillService sut = BuildService();

        // Act
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Assert
        _validator.CallsByToken.ShouldBeEmpty();
        (await LoadAccountAsync(accountId)).TokenExpiry.ShouldBe(new TokenExpiry.NotChecked());
    }

    [Fact]
    public async Task WhenAccountTokenIsUnreadable_ItIsSkipped()
    {
        // Arrange — ciphertext that cannot be decrypted loads as an Unreadable token.
        await SeedAccountAsync("my-org", "ghp_token");
        await using (FoundryDbContext db = CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE accounts SET token = {"not-ciphertext"} WHERE name = {"my-org"}",
                TestContext.Current.CancellationToken);
        }

        AccountTokenExpiryBackfillService sut = BuildService();

        // Act
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Assert
        _validator.CallsByToken.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAccountHasNoToken_ItIsSkipped()
    {
        // Arrange
        await using (FoundryDbContext db = CreateDbContext())
        {
            db.Set<Credential>().Add(GitHubCredential.Create(
                AccountName.Create("my-org").ValueOrThrow(),
                null,
                BaseUrl.Create("https://github.com").ValueOrThrow()));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        AccountTokenExpiryBackfillService sut = BuildService();

        // Act
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Assert
        _validator.CallsByToken.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenExpiryIsAlreadyKnown_AccountIsNotRead()
    {
        // Arrange
        await SeedAccountAsync("my-org", "ghp_token", expiry: new TokenExpiry.Unavailable());
        AccountTokenExpiryBackfillService sut = BuildService();

        // Act
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Assert
        _validator.CallsByToken.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenOneAccountFails_TheOthersAreStillFilledIn()
    {
        // Arrange
        CredentialId failing = await SeedAccountAsync("failing-org", "ghp_failing");
        CredentialId healthy = await SeedAccountAsync("healthy-org", "ghp_healthy");
        _validator.Throw("ghp_failing", new InvalidOperationException("boom"));
        _validator.Respond("ghp_healthy", TokenValidationOutcome.Authenticated("healthy-org", ExpiresOn));
        AccountTokenExpiryBackfillService sut = BuildService();

        // Act
        await sut.BackfillAsync(TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(failing)).TokenExpiry.ShouldBe(new TokenExpiry.NotChecked());
        (await LoadAccountAsync(healthy)).TokenExpiry.ShouldBe(ExpiresOn);
    }

    private static TokenValidationOutcome OutcomeWithoutAReading(string outcome) => outcome switch
    {
        nameof(TokenValidationOutcome.AuthenticationFailedOutcome) => TokenValidationOutcome.AuthenticationFailed(),
        nameof(TokenValidationOutcome.IdentityUnresolvedOutcome) => TokenValidationOutcome.IdentityUnresolved(),
        nameof(TokenValidationOutcome.ProviderMismatchOutcome) => TokenValidationOutcome.ProviderMismatch("gitlab"),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private FoundryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private async Task<CredentialId> SeedAccountAsync(
        string name,
        string token,
        TokenExpiry? expiry = null,
        DateTimeOffset? invalidSince = null)
    {
        await using FoundryDbContext db = CreateDbContext();
        GitHubCredential account = GitHubCredential.Create(
            AccountName.Create(name).ValueOrThrow(),
            new IssuedToken(token, expiry ?? new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());

        if (invalidSince is { } since)
        {
            account.MarkTokenInvalid(since);
        }

        db.Set<Credential>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return account.Id;
    }

    private async Task<Credential> LoadAccountAsync(CredentialId id)
    {
        await using FoundryDbContext db = CreateDbContext();
        return await db.Set<Credential>().AsNoTracking().SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);
    }

    private AccountTokenExpiryBackfillService BuildService()
    {
        SqliteConnection connection = _connection;
        ScriptedTokenValidator validator = _validator;
        ServiceCollection services = new();

        services.AddScoped<FoundryDbContext>(_ => new FoundryDbContext(
            new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(connection).Options));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddLogging();
        services.AddScoped<IDomainEventDispatcher, NullDomainEventDispatcher>();
        services.AddScoped<IAccountTokenValidator>(_ => validator);
        services.AddScoped<AccountTokenVerdicts>();

        ServiceProvider provider = services.BuildServiceProvider();
        return new AccountTokenExpiryBackfillService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AccountTokenExpiryBackfillService>.Instance);
    }

    private sealed class ScriptedTokenValidator : IAccountTokenValidator
    {
        private readonly Dictionary<string, Func<Result<TokenValidationOutcome>>> _responses = [];

        public Dictionary<string, int> CallsByToken { get; } = [];

        public void Respond(string token, TokenValidationOutcome outcome) =>
            _responses[token] = () => Result<TokenValidationOutcome>.Ok(outcome);

        public void Respond(string token, Error error) =>
            _responses[token] = () => Result<TokenValidationOutcome>.Fail(error);

        public void Throw(string token, Exception exception) =>
            _responses[token] = () => throw exception;

        public Task<Result<TokenValidationOutcome>> ValidateAsync(
            Credential credential,
            string token,
            CancellationToken cancellationToken)
        {
            CallsByToken[token] = CallsByToken.GetValueOrDefault(token) + 1;
            return Task.FromResult(_responses[token]());
        }
    }
}
