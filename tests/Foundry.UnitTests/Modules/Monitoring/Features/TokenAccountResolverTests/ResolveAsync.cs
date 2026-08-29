using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.TokenAccountResolverTests;

public sealed class ResolveAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public ResolveAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private TokenAccountResolver BuildSut(
        IQueryHandler<ValidateToken.Query, ValidateToken.Response>? validateToken = null,
        INamespaceDeriver? deriver = null) =>
        new(
            _dbContext,
            validateToken ?? new StubValidateTokenHandler("resolved-user"),
            deriver ?? new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([], [])));

    private async Task<GitHubCredential> SeedCredentialAsync(string accountName = "original-user")
    {
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create(accountName, "ghp_original", baseUrl);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return credential;
    }

    // ── Behavior 1: valid token with free derived namespaces → Resolved ──────────────

    [Fact]
    public async Task WhenValidToken_FreeNamespaces_ReturnsResolved()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        Namespace aliceNs = Namespace.Create("alice").ValueOrThrow();

        TokenAccountResolver sut = BuildSut(
            validateToken: new StubValidateTokenHandler("alice"),
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([aliceNs], [])));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            credential,
            token: "ghp_token",
            baseUrl,
            isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert
        TokenResolution.Resolved resolved = result.ShouldBeOfType<TokenResolution.Resolved>();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.AccountName.ShouldBe("alice"),
            () => resolved.Namespaces.ShouldContain(n => n.Value == "alice"));
    }

    // ── Behavior 2: token-validation failures → correct error codes ──────────────────

    [Fact]
    public async Task WhenAuthenticatedWithMissingScopes_RejectsWithInvalidToken()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        TokenAccountResolver sut = BuildSut(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.Authenticated,
                AccountName: "updated-user",
                MissingScopes: ["repo", "write:packages"]));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            credential, "ghp_token", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert
        TokenResolution.Rejected rejected = result.ShouldBeOfType<TokenResolution.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.InvalidTokenCode);
    }

    [Fact]
    public async Task WhenAuthenticationFailed_RejectsWithInvalidToken()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        TokenAccountResolver sut = BuildSut(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.AuthenticationFailed,
                AccountName: null,
                MissingScopes: []));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            credential, "ghp_token", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert
        TokenResolution.Rejected rejected = result.ShouldBeOfType<TokenResolution.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.InvalidTokenCode);
    }

    [Fact]
    public async Task WhenIdentityUnresolved_RejectsWithUnresolvedIdentity()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        TokenAccountResolver sut = BuildSut(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.IdentityUnresolved,
                AccountName: null,
                MissingScopes: []));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            credential, "ghp_token", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert
        TokenResolution.Rejected rejected = result.ShouldBeOfType<TokenResolution.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.UnresolvedIdentityCode);
    }

    [Fact]
    public async Task WhenScopesUnverifiableWithNullName_RejectsWithUnresolvedIdentity()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        TokenAccountResolver sut = BuildSut(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.ScopesUnverifiable,
                AccountName: null,
                MissingScopes: []));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            credential, "ghp_token", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert
        TokenResolution.Rejected rejected = result.ShouldBeOfType<TokenResolution.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.UnresolvedIdentityCode);
    }

    [Fact]
    public async Task WhenScopesUnverifiableWithName_ReturnsResolved()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        TokenAccountResolver sut = BuildSut(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.ScopesUnverifiable,
                AccountName: "unverifiable-user",
                MissingScopes: []));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            credential, "ghp_token", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert
        TokenResolution.Resolved resolved = result.ShouldBeOfType<TokenResolution.Resolved>();
        resolved.AccountName.ShouldBe("unverifiable-user");
    }

    [Fact]
    public async Task WhenProviderMismatch_RejectsWithProviderMismatchError()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        TokenAccountResolver sut = BuildSut(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.ProviderMismatch,
                AccountName: null,
                MissingScopes: [],
                DetectedProvider: "gitlab"));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            credential, "ghp_token", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert
        TokenResolution.Rejected rejected = result.ShouldBeOfType<TokenResolution.Rejected>();
        rejected.ShouldSatisfyAllConditions(
            () => rejected.Error.Code.ShouldBe(CredentialErrors.ProviderMismatchCode),
            () => rejected.Error.Message.ShouldContain("gitlab"));
    }

    // ── Behavior 3: derivation Unavailable → Rejected ────────────────────────────────

    [Fact]
    public async Task WhenDerivationUnavailable_RejectsWithNamespaceDerivationUnavailable()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        TokenAccountResolver sut = BuildSut(
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Unavailable()));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            credential, "ghp_token", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert
        TokenResolution.Rejected rejected = result.ShouldBeOfType<TokenResolution.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.NamespaceDerivationUnavailableCode);
    }

    // ── Behavior 4: empty derived set → Resolved (not ClaimedElsewhere) ─────────────

    [Fact]
    public async Task WhenEmptyDerivedSet_ReturnsResolved()
    {
        // Arrange — another credential claims a namespace, but derived set is empty.
        // The fully-claimed guard must NOT fire on an empty derived set (AC#4 empty≠unavailable).
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        GitHubCredential holder = GitHubCredential.Create("holder-user", "ghp_holder", baseUrl);
        Namespace holderNs = Namespace.Create("some-org").ValueOrThrow();
        holder.SetNamespaces([holderNs]);
        _dbContext.Set<Credential>().Add(holder);

        GitHubCredential subject = GitHubCredential.Create("subject-user", "ghp_subject", baseUrl);
        _dbContext.Set<Credential>().Add(subject);

        await _dbContext.SaveChangesAsync(CancellationToken.None);

        TokenAccountResolver sut = BuildSut(
            validateToken: new StubValidateTokenHandler("subject-user"),
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([], [])));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            subject, "ghp_new_subject", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert — empty derived set must not trigger the fully-claimed guard
        result.ShouldBeOfType<TokenResolution.Resolved>();
    }

    // ── Behavior 5: duplicate login intersecting owner with all-claimed → Rejected ───

    [Fact]
    public async Task WhenDuplicateLoginIntersectsOwner_AllClaimed_RejectsDuplicateAccount()
    {
        // Arrange — seed first-user claiming "first-user"; second-user has no namespaces.
        // Rotating second with a token resolving to "first-user" and deriving "first-user" collides.
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        GitHubCredential first = GitHubCredential.Create("first-user", "ghp_first", baseUrl);
        Namespace firstNs = Namespace.Create("first-user").ValueOrThrow();
        first.SetNamespaces([firstNs]);
        _dbContext.Set<Credential>().Add(first);

        GitHubCredential second = GitHubCredential.Create("second-user", "ghp_second", baseUrl);
        _dbContext.Set<Credential>().Add(second);

        await _dbContext.SaveChangesAsync(CancellationToken.None);

        TokenAccountResolver sut = BuildSut(
            validateToken: new StubValidateTokenHandler("first-user"),
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([firstNs], [])));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            second, "ghp_colliding", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert
        TokenResolution.Rejected rejected = result.ShouldBeOfType<TokenResolution.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.DuplicateAccountCode);
    }

    // ── Behavior 6: own namespace excludes self → Resolved ───────────────────────────

    [Fact]
    public async Task WhenOwnNamespaceExcludesSelf_ReturnsResolved()
    {
        // Arrange — a single account claiming its own namespace.
        // Rotating with a token deriving the same namespace must succeed because the account
        // is excluded from the claimed-by-others check.
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("alice", "ghp_old", baseUrl);
        Namespace aliceNs = Namespace.Create("alice").ValueOrThrow();
        credential.SetNamespaces([aliceNs]);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        TokenAccountResolver sut = BuildSut(
            validateToken: new StubValidateTokenHandler("alice"),
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([aliceNs], [])));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            credential, "ghp_new", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert — legitimate own-namespace rotation succeeds (not flagged as duplicate)
        result.ShouldBeOfType<TokenResolution.Resolved>();
    }

    // ── Behavior 7: all derived claimed by a different login → ClaimedElsewhere ─────

    [Fact]
    public async Task WhenAllDerivedClaimedByDifferentLogin_ReturnsClaimedElsewhere()
    {
        // Arrange — holder claims "org-a"; subject's new token derives only "org-a".
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        GitHubCredential holder = GitHubCredential.Create("holder-user", "ghp_holder", baseUrl);
        Namespace orgNs = Namespace.Create("org-a").ValueOrThrow();
        holder.SetNamespaces([orgNs]);
        _dbContext.Set<Credential>().Add(holder);

        GitHubCredential subject = GitHubCredential.Create("subject-user", "ghp_subject", baseUrl);
        subject.SetNamespaces([]);
        _dbContext.Set<Credential>().Add(subject);

        await _dbContext.SaveChangesAsync(CancellationToken.None);

        TokenAccountResolver sut = BuildSut(
            validateToken: new StubValidateTokenHandler("subject-user"),
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([orgNs], [])));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            subject, "ghp_new_subject", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert — ClaimedElsewhere with server-composed error message (AC7)
        TokenResolution.ClaimedElsewhere claimedElsewhere =
            result.ShouldBeOfType<TokenResolution.ClaimedElsewhere>();
        claimedElsewhere.Error.Code.ShouldBe(CredentialErrors.NamespaceClaimedElsewhereCode);
        claimedElsewhere.Error.Message.ShouldContain("org-a");
        claimedElsewhere.Error.Message.ShouldContain("holder-user");
    }

    // ── Behavior 8: partial overlap (some claimed, some free) → Resolved ─────────────

    [Fact]
    public async Task WhenPartialDerivedSetClaimedByOthers_ReturnsResolved()
    {
        // Arrange — holder claims "claimed-org"; subject derives both "claimed-org" and "free-org".
        // Guard must NOT fire — "free-org" is available, so resolution can proceed.
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        GitHubCredential holder = GitHubCredential.Create("holder-user", "ghp_holder", baseUrl);
        Namespace claimedNs = Namespace.Create("claimed-org").ValueOrThrow();
        holder.SetNamespaces([claimedNs]);
        _dbContext.Set<Credential>().Add(holder);

        GitHubCredential subject = GitHubCredential.Create("subject-user", "ghp_subject", baseUrl);
        _dbContext.Set<Credential>().Add(subject);

        await _dbContext.SaveChangesAsync(CancellationToken.None);

        Namespace freeNs = Namespace.Create("free-org").ValueOrThrow();
        TokenAccountResolver sut = BuildSut(
            validateToken: new StubValidateTokenHandler("subject-user"),
            deriver: new StubNamespaceDeriver(
                new NamespaceDerivationOutcome.Derived([claimedNs, freeNs], [])));

        // Act
        TokenResolution result = await sut.ResolveAsync(
            subject, "ghp_new_subject", baseUrl, isGitLab: false,
            TestContext.Current.CancellationToken);

        // Assert — partial overlap must not trigger the fully-claimed guard
        TokenResolution.Resolved resolved = result.ShouldBeOfType<TokenResolution.Resolved>();
        resolved.AccountName.ShouldBe("subject-user");
    }

    // ── Stubs and fakes ─────────────────────────────────────────────────────────────

    private sealed class StubValidateTokenHandler(string accountName)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken)
        {
            ValidateToken.Response response = new(
                Kind: ValidateToken.Kinds.Authenticated,
                AccountName: accountName,
                MissingScopes: [],
                DetectedProvider: null);
            return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
        }
    }

    private sealed class KindValidateTokenHandler(
        string kind,
        string? AccountName,
        IReadOnlyList<string> MissingScopes,
        string? DetectedProvider = null)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken)
        {
            ValidateToken.Response response = new(
                Kind: kind,
                AccountName: AccountName,
                MissingScopes: MissingScopes,
                DetectedProvider: DetectedProvider);
            return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
        }
    }

    private sealed class StubNamespaceDeriver(NamespaceDerivationOutcome outcome) : INamespaceDeriver
    {
        public Task<NamespaceDerivationOutcome> DeriveAsync(
            Credential credential,
            CancellationToken cancellationToken) =>
            Task.FromResult(outcome);

        public Task<NamespaceDerivationOutcome> DeriveAsync(
            Uri apiBaseUrl,
            string token,
            bool isGitLab,
            CancellationToken cancellationToken) =>
            Task.FromResult(outcome);
    }
}
