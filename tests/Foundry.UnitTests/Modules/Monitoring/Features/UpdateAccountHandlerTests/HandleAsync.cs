using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Services;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.UpdateAccountHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public HandleAsync()
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

    private static ProviderHostGuard BuildPassingGuard()
    {
        FakeHostAddressResolver resolver = new FakeHostAddressResolver()
            .WithAddresses("github.com", System.Net.IPAddress.Parse("140.82.121.4"))
            .WithAddresses("gitlab.com", System.Net.IPAddress.Parse("172.65.251.78"))
            .WithAddresses("github.enterprise.com", System.Net.IPAddress.Parse("140.82.121.10"));
        StubGlobalSettingsQueries settings = new(allowedHosts: ["github.enterprise.com"]);
        return new ProviderHostGuard(settings, resolver);
    }

    private UpdateAccount.Handler BuildHandler(
        IQueryHandler<ValidateToken.Query, ValidateToken.Response>? validateToken = null,
        INamespaceDeriver? deriver = null,
        IRepositoryEligibilityEvaluator? evaluator = null,
        ProviderHostGuard? hostGuard = null)
    {
        RepositoryEligibilityDiffer differ = new(
            _dbContext,
            evaluator ?? new NoOpEligibilityEvaluator());

        CredentialRotationService rotationService = new(
            _dbContext,
            differ);

        TokenAccountResolver tokenAccountResolver = new(
            _dbContext,
            validateToken ?? new StubValidateTokenHandler("updated-user"),
            deriver ?? new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([], [])));

        return new UpdateAccount.Handler(_dbContext, tokenAccountResolver, rotationService, hostGuard ?? BuildPassingGuard());
    }

    private async Task<GitHubCredential> SeedCredentialAsync(string accountName = "original-user")
    {
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create(accountName, "ghp_original", baseUrl);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return credential;
    }

    [Fact]
    public async Task WhenTokenSupplied_ReturnsAffectedReposFromRotationPath()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        Namespace aliceNs = Namespace.Create("alice").ValueOrThrow();

        AssignedEligibilityEvaluator evaluator = new(new Dictionary<string, RepositoryEligibility>());
        UpdateAccount.Handler handler = BuildHandler(
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([aliceNs], [])),
            evaluator: evaluator);

        UpdateAccount.Command command = new(credential.Id, "https://github.com", "ghp_newtoken");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Updated updated = outcome.ShouldBeOfType<UpdateAccount.Outcome.Updated>();
        updated.Value.ShouldSatisfyAllConditions(
            () => updated.Value.Credential.Id.ShouldBe(credential.Id.Value),
            () => updated.Value.AffectedRepositories.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenTokenSupplied_DerivesAccountNameFromTokenResponse()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync("original-user");
        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new StubValidateTokenHandler("new-account-name"));

        UpdateAccount.Command command = new(credential.Id, "https://github.com", "ghp_newtoken");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Updated updated = outcome.ShouldBeOfType<UpdateAccount.Outcome.Updated>();
        updated.Value.Credential.Name.ShouldBe("new-account-name");
    }

    [Fact]
    public async Task WhenNoTokenSupplied_SavesWithoutRotationAndReturnsEmptyAffected()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        UpdateAccount.Handler handler = BuildHandler();

        UpdateAccount.Command command = new(credential.Id, "https://github.com", Token: null);

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Updated updated = outcome.ShouldBeOfType<UpdateAccount.Outcome.Updated>();
        updated.Value.ShouldSatisfyAllConditions(
            () => updated.Value.Credential.Id.ShouldBe(credential.Id.Value),
            () => updated.Value.AffectedRepositories.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenNoTokenSupplied_PersistsBaseUrlChange()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        UpdateAccount.Handler handler = BuildHandler();

        UpdateAccount.Command command = new(credential.Id, "https://github.enterprise.com", Token: null);

        // Act
        await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        Credential? stored = await _dbContext.Set<Credential>()
            .FirstOrDefaultAsync(c => c.Id == credential.Id, TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.BaseUrl.Value.Host.ShouldBe("github.enterprise.com");
    }

    [Fact]
    public async Task WhenNoTokenSupplied_DeriverIsNeverCalledEvenIfItWouldReturnUnavailable()
    {
        // Arrange — deriver returns Unavailable; with no token, derivation must not run.
        GitHubCredential credential = await SeedCredentialAsync();
        UpdateAccount.Handler handler = BuildHandler(
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Unavailable()));

        UpdateAccount.Command command = new(credential.Id, "https://github.com", Token: null);

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert — no token path succeeds regardless of what the deriver would return
        outcome.ShouldBeOfType<UpdateAccount.Outcome.Updated>();
    }

    [Fact]
    public async Task WhenTokenSupplied_DerivationUnavailable_RejectsAndLeavesCredentialUnchanged()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("original-user", "ghp_original", baseUrl);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        UpdateAccount.Handler handler = BuildHandler(
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Unavailable()));

        UpdateAccount.Command command = new(credential.Id, "https://github.com", "ghp_newtoken");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert — rejected with the correct error code
        UpdateAccount.Outcome.Rejected rejected = outcome.ShouldBeOfType<UpdateAccount.Outcome.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.NamespaceDerivationUnavailableCode);

        // Assert — credential is unchanged in the database (AC#7)
        Credential? stored = await _dbContext.Set<Credential>()
            .FirstOrDefaultAsync(c => c.Id == credential.Id, TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.ShouldSatisfyAllConditions(
            () => stored.Token.ShouldBe("ghp_original"),
            () => stored.Name.ShouldBe("original-user"),
            () => stored.BaseUrl.Value.Host.ShouldBe("github.com"));
    }

    [Fact]
    public async Task WhenTokenSupplied_DuplicateLoginIntersectsOwner_ReturnsDuplicateError()
    {
        // Arrange — seed two accounts: "first-user" claiming "first-user", "second-user" with no namespaces.
        // Then attempt to rotate second with a token resolving to "first-user" and deriving "first-user".
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        GitHubCredential first = GitHubCredential.Create("first-user", "ghp_first", baseUrl);
        Namespace firstNs = Namespace.Create("first-user").ValueOrThrow();
        first.SetNamespaces([firstNs]);
        _dbContext.Set<Credential>().Add(first);

        GitHubCredential second = GitHubCredential.Create("second-user", "ghp_second", baseUrl);
        _dbContext.Set<Credential>().Add(second);

        await _dbContext.SaveChangesAsync(CancellationToken.None);

        // Token resolves to "first-user" and derives namespace "first-user" — same as first account's claim.
        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new StubValidateTokenHandler("first-user"),
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([firstNs], [])));

        UpdateAccount.Command command = new(second.Id, "https://github.com", "ghp_colliding");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert — rejected as duplicate
        UpdateAccount.Outcome.Rejected rejected = outcome.ShouldBeOfType<UpdateAccount.Outcome.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.DuplicateAccountCode);

        // Assert — second account's token/name/baseUrl are unchanged in the database (AC#7)
        Credential? stored = await _dbContext.Set<Credential>()
            .FirstOrDefaultAsync(c => c.Id == second.Id, TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.ShouldSatisfyAllConditions(
            () => stored.Token.ShouldBe("ghp_second"),
            () => stored.Name.ShouldBe("second-user"),
            () => stored.BaseUrl.Value.Host.ShouldBe("github.com"));
    }

    [Fact]
    public async Task WhenTokenSupplied_OwnNamespaceExcludesSelf_Succeeds()
    {
        // Arrange — seed a single account claiming its own namespace.
        // Rotating with a token deriving the same namespace must succeed because the account
        // is excluded from the duplicate check (AC#3).
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("alice", "ghp_old", baseUrl);
        Namespace aliceNs = Namespace.Create("alice").ValueOrThrow();
        credential.SetNamespaces([aliceNs]);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new StubValidateTokenHandler("alice"),
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([aliceNs], [])));

        UpdateAccount.Command command = new(credential.Id, "https://github.com", "ghp_new");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert — legitimate own-namespace rotation succeeds (not flagged as a duplicate)
        outcome.ShouldBeOfType<UpdateAccount.Outcome.Updated>();
    }

    [Fact]
    public async Task WhenDerivedNamespaceClaimedByOtherOnRotate_SubtractsItSilently()
    {
        // Arrange — seed two credentials; rotate the second whose derived set overlaps first's namespace.
        // Never-steal semantics: the shared namespace is subtracted, not an error.
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        GitHubCredential first = GitHubCredential.Create("first-user", "ghp_first", baseUrl);
        Namespace sharedNs = Namespace.Create("shared-org").ValueOrThrow();
        Namespace ownNs = Namespace.Create("second-org").ValueOrThrow();
        first.SetNamespaces([sharedNs]);
        _dbContext.Set<Credential>().Add(first);

        GitHubCredential second = GitHubCredential.Create("second-user", "ghp_second", baseUrl);
        _dbContext.Set<Credential>().Add(second);

        await _dbContext.SaveChangesAsync(CancellationToken.None);

        // The deriver returns both namespaces; "shared-org" is already held by first.
        // "second-user" != "first-user" so the duplicate guard does not fire.
        UpdateAccount.Handler handler = BuildHandler(
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([sharedNs, ownNs], [])));

        UpdateAccount.Command command = new(second.Id, "https://github.com", "ghp_newtoken");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert — rotation succeeds; second only ends up with its own namespace
        outcome.ShouldBeOfType<UpdateAccount.Outcome.Updated>();
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == second.Id, CancellationToken.None);
        stored.ShouldNotBeNull();
        stored.Namespaces.ShouldContain(n => n.Value == "second-org");
        stored.Namespaces.ShouldNotContain(n => n.Value == "shared-org");
    }

    [Fact]
    public async Task WhenTokenSupplied_SiblingSharesLoginButOwnNamespaceRetained_Succeeds()
    {
        // Arrange — two credentials on github.com both named "karirafn";
        // A claims "karirafn", B claims "Kraftlyftingasamband-Islands".
        // Rotating A with a token that resolves to "karirafn" and derives BOTH namespaces must
        // succeed — RotateAsync will subtract the sibling-owned namespace, leaving A with only "karirafn".
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        Namespace karirafnNs = Namespace.Create("karirafn").ValueOrThrow();
        Namespace kraftNs = Namespace.Create("Kraftlyftingasamband-Islands").ValueOrThrow();

        GitHubCredential credentialA = GitHubCredential.Create("karirafn", "ghp_old_a", baseUrl);
        credentialA.SetNamespaces([karirafnNs]);
        _dbContext.Set<Credential>().Add(credentialA);

        GitHubCredential credentialB = GitHubCredential.Create("karirafn", "ghp_b", baseUrl);
        credentialB.SetNamespaces([kraftNs]);
        _dbContext.Set<Credential>().Add(credentialB);

        await _dbContext.SaveChangesAsync(CancellationToken.None);

        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new StubValidateTokenHandler("karirafn"),
            deriver: new StubNamespaceDeriver(
                new NamespaceDerivationOutcome.Derived([karirafnNs, kraftNs], [])));

        UpdateAccount.Command command = new(credentialA.Id, "https://github.com", "ghp_new_a");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<UpdateAccount.Outcome.Updated>();

        Credential? storedA = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == credentialA.Id, CancellationToken.None);
        storedA.ShouldNotBeNull();
        storedA.ShouldSatisfyAllConditions(
            () => storedA.Token.ShouldBe("ghp_new_a"),
            () => storedA.Namespaces.Count.ShouldBe(1),
            () => storedA.Namespaces.ShouldContain(n => n.Value == "karirafn"));

        Credential? storedB = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == credentialB.Id, CancellationToken.None);
        storedB.ShouldNotBeNull();
        storedB.Namespaces.ShouldContain(n => n.Value == "Kraftlyftingasamband-Islands");
    }

    [Fact]
    public async Task WhenAllDerivedNamespacesClaimedByDifferentLogin_ReturnsClaimedElsewhere()
    {
        // Arrange — seed holder claiming "org-a", and subject with no namespaces.
        // Subject's new token resolves to "subject-user" (different login from holder)
        // but derives only "org-a" — already fully claimed by holder.
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        GitHubCredential holder = GitHubCredential.Create("holder-user", "ghp_holder", baseUrl);
        Namespace orgNs = Namespace.Create("org-a").ValueOrThrow();
        holder.SetNamespaces([orgNs]);
        _dbContext.Set<Credential>().Add(holder);

        GitHubCredential subject = GitHubCredential.Create("subject-user", "ghp_subject", baseUrl);
        subject.SetNamespaces([]);
        _dbContext.Set<Credential>().Add(subject);

        await _dbContext.SaveChangesAsync(CancellationToken.None);

        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new StubValidateTokenHandler("subject-user"),
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([orgNs], [])));

        UpdateAccount.Command command = new(subject.Id, "https://github.com", "ghp_new_subject");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert — returns ClaimedElsewhere with the server-composed error message
        UpdateAccount.Outcome.ClaimedElsewhere claimedElsewhere =
            outcome.ShouldBeOfType<UpdateAccount.Outcome.ClaimedElsewhere>();
        claimedElsewhere.Error.Code.ShouldBe(CredentialErrors.NamespaceClaimedElsewhereCode);
        claimedElsewhere.Error.Message.ShouldContain("org-a");
        claimedElsewhere.Error.Message.ShouldContain("holder-user");

        // Assert — subject's namespaces are unchanged (no strand)
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == subject.Id, CancellationToken.None);
        stored.ShouldNotBeNull();
        stored.Namespaces.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPartialDerivedSetClaimedByOthers_ReturnsUpdatedWithRetainedNamespace()
    {
        // Arrange — holder claims "claimed-org"; subject derives both "claimed-org" and "free-org".
        // Guard must NOT fire — "free-org" is available, so rotation can proceed and retain it.
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        GitHubCredential holder = GitHubCredential.Create("holder-user", "ghp_holder", baseUrl);
        Namespace claimedNs = Namespace.Create("claimed-org").ValueOrThrow();
        holder.SetNamespaces([claimedNs]);
        _dbContext.Set<Credential>().Add(holder);

        GitHubCredential subject = GitHubCredential.Create("subject-user", "ghp_subject", baseUrl);
        _dbContext.Set<Credential>().Add(subject);

        await _dbContext.SaveChangesAsync(CancellationToken.None);

        Namespace freeNs = Namespace.Create("free-org").ValueOrThrow();
        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new StubValidateTokenHandler("subject-user"),
            deriver: new StubNamespaceDeriver(
                new NamespaceDerivationOutcome.Derived([claimedNs, freeNs], [])));

        UpdateAccount.Command command = new(subject.Id, "https://github.com", "ghp_new_subject");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert — partial overlap must not trigger the fully-claimed guard
        outcome.ShouldBeOfType<UpdateAccount.Outcome.Updated>();

        // Assert — subject retains only the free namespace after rotation
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == subject.Id, CancellationToken.None);
        stored.ShouldNotBeNull();
        stored.Namespaces.ShouldSatisfyAllConditions(
            () => stored.Namespaces.ShouldContain(n => n.Value == "free-org"),
            () => stored.Namespaces.ShouldNotContain(n => n.Value == "claimed-org"));
    }

    [Fact]
    public async Task WhenDerivedSetIsEmpty_DoesNotReturnClaimedElsewhere()
    {
        // Arrange — another credential claims a namespace, but the deriver returns an empty set.
        // The fully-claimed guard must NOT fire on an empty derived set.
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();

        GitHubCredential holder = GitHubCredential.Create("holder-user", "ghp_holder", baseUrl);
        Namespace holderNs = Namespace.Create("some-org").ValueOrThrow();
        holder.SetNamespaces([holderNs]);
        _dbContext.Set<Credential>().Add(holder);

        GitHubCredential subject = GitHubCredential.Create("subject-user", "ghp_subject", baseUrl);
        _dbContext.Set<Credential>().Add(subject);

        await _dbContext.SaveChangesAsync(CancellationToken.None);

        // Deriver returns empty — zero derived namespaces
        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new StubValidateTokenHandler("subject-user"),
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([], [])));

        UpdateAccount.Command command = new(subject.Id, "https://github.com", "ghp_new_subject");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert — empty derived set must not trigger the fully-claimed guard
        outcome.ShouldBeOfType<UpdateAccount.Outcome.Updated>();
    }

    [Fact]
    public async Task WhenCredentialNotFound_ReturnsNotFoundError()
    {
        // Arrange
        UpdateAccount.Handler handler = BuildHandler();
        CredentialId nonExistentId = CredentialId.New();
        UpdateAccount.Command command = new(nonExistentId, "https://github.com", Token: null);

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Rejected rejected = outcome.ShouldBeOfType<UpdateAccount.Outcome.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.NotFoundCode);
    }

    [Fact]
    public async Task WhenAuthenticatedWithMissingScopes_RejectsWithInvalidToken()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.Authenticated,
                AccountName: "updated-user",
                MissingScopes: ["repo", "write:packages"]));
        UpdateAccount.Command command = new(credential.Id, "https://github.com", "ghp_newtoken");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Rejected rejected = outcome.ShouldBeOfType<UpdateAccount.Outcome.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.InvalidTokenCode);
    }

    [Fact]
    public async Task WhenScopesUnverifiable_ProceedsToUpdateCredential()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.ScopesUnverifiable,
                AccountName: "unverifiable-user",
                MissingScopes: []));
        UpdateAccount.Command command = new(credential.Id, "https://github.com", "ghp_newtoken");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Updated updated = outcome.ShouldBeOfType<UpdateAccount.Outcome.Updated>();
        updated.Value.Credential.Name.ShouldBe("unverifiable-user");
    }

    [Fact]
    public async Task WhenProviderMismatch_RejectsWithProviderMismatchError()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.ProviderMismatch,
                AccountName: null,
                MissingScopes: [],
                DetectedProvider: "gitlab"));
        UpdateAccount.Command command = new(credential.Id, "https://github.com", "ghp_newtoken");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Rejected rejected = outcome.ShouldBeOfType<UpdateAccount.Outcome.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.ProviderMismatchCode);
        rejected.Error.Message.ShouldContain("gitlab");
    }

    [Fact]
    public async Task WhenAuthenticationFailed_RejectsWithInvalidToken()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.AuthenticationFailed,
                AccountName: null,
                MissingScopes: []));
        UpdateAccount.Command command = new(credential.Id, "https://github.com", "ghp_newtoken");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Rejected rejected = outcome.ShouldBeOfType<UpdateAccount.Outcome.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.InvalidTokenCode);
    }

    [Fact]
    public async Task WhenIdentityUnresolved_RejectsWithUnresolvedIdentity()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.IdentityUnresolved,
                AccountName: null,
                MissingScopes: []));
        UpdateAccount.Command command = new(credential.Id, "https://github.com", "ghp_newtoken");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Rejected rejected = outcome.ShouldBeOfType<UpdateAccount.Outcome.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.UnresolvedIdentityCode);
    }

    [Fact]
    public async Task WhenScopesUnverifiableWithNullAccountName_RejectsWithUnresolvedIdentity()
    {
        // Arrange — ScopesUnverifiable with a null AccountName must map to UnresolvedIdentity,
        // not silently fall through to InvalidToken via the default arm.
        GitHubCredential credential = await SeedCredentialAsync();
        UpdateAccount.Handler handler = BuildHandler(
            validateToken: new KindValidateTokenHandler(
                ValidateToken.Kinds.ScopesUnverifiable,
                AccountName: null,
                MissingScopes: []));
        UpdateAccount.Command command = new(credential.Id, "https://github.com", "ghp_newtoken");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Rejected rejected = outcome.ShouldBeOfType<UpdateAccount.Outcome.Rejected>();
        rejected.Error.Code.ShouldBe(CredentialErrors.UnresolvedIdentityCode);
    }

    [Fact]
    public async Task WhenHostNotAllowed_RejectsBeforeDbLookup()
    {
        // Arrange — use a non-existent credential ID; if the guard runs after the DB fetch,
        // the handler would return NotFound, not HostNotAllowed.
        FakeHostAddressResolver resolver = new FakeHostAddressResolver()
            .WithAddresses("attacker.example.com", System.Net.IPAddress.Parse("93.184.216.34"));
        StubGlobalSettingsQueries settings = new(allowedHosts: []);
        ProviderHostGuard guard = new(settings, resolver);

        UpdateAccount.Handler handler = BuildHandler(hostGuard: guard);
        CredentialId nonExistentId = CredentialId.New();
        UpdateAccount.Command command = new(nonExistentId, "https://attacker.example.com", Token: null);

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert — guard fires first; NotFound is never reached
        UpdateAccount.Outcome.Rejected rejected = outcome.ShouldBeOfType<UpdateAccount.Outcome.Rejected>();
        rejected.Error.Code.ShouldBe("ProviderHost.NotAllowed");
    }

    [Fact]
    public async Task WhenHostNotAllowed_WithToken_RejectsBeforeTokenResolution()
    {
        // Arrange — "attacker.example.com" not in implicit set; allowlist empty
        GitHubCredential credential = await SeedCredentialAsync();

        FakeHostAddressResolver resolver = new FakeHostAddressResolver()
            .WithAddresses("attacker.example.com", System.Net.IPAddress.Parse("93.184.216.34"));
        StubGlobalSettingsQueries settings = new(allowedHosts: []);
        ProviderHostGuard guard = new(settings, resolver);

        SpyValidateTokenHandler spy = new("attacker-user");
        UpdateAccount.Handler handler = BuildHandler(validateToken: spy, hostGuard: guard);

        UpdateAccount.Command command = new(credential.Id, "https://attacker.example.com", "ghp_stolen");

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Rejected rejected = outcome.ShouldBeOfType<UpdateAccount.Outcome.Rejected>();
        rejected.Error.Code.ShouldBe("ProviderHost.NotAllowed");
        spy.WasCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenHostNotAllowed_WithNullToken_RejectsWithHostGuardError()
    {
        // Arrange — null-token (URL-only) update to disallowed host must also be blocked
        GitHubCredential credential = await SeedCredentialAsync();

        FakeHostAddressResolver resolver = new FakeHostAddressResolver()
            .WithAddresses("attacker.example.com", System.Net.IPAddress.Parse("93.184.216.34"));
        StubGlobalSettingsQueries settings = new(allowedHosts: []);
        ProviderHostGuard guard = new(settings, resolver);

        UpdateAccount.Handler handler = BuildHandler(hostGuard: guard);

        UpdateAccount.Command command = new(credential.Id, "https://attacker.example.com", Token: null);

        // Act
        UpdateAccount.Outcome outcome = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        UpdateAccount.Outcome.Rejected rejected = outcome.ShouldBeOfType<UpdateAccount.Outcome.Rejected>();
        rejected.Error.Code.ShouldBe("ProviderHost.NotAllowed");
    }

    // Stubs and fakes

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

    private sealed class NoOpEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class AssignedEligibilityEvaluator(
        Dictionary<string, RepositoryEligibility> assignments) : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            if (assignments.TryGetValue(repo.Slug.FullPath, out RepositoryEligibility? eligibility))
            {
                repo.SetEligibility(eligibility);
            }

            return Task.CompletedTask;
        }

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
        {
            if (assignments.TryGetValue(repo.Slug.FullPath, out RepositoryEligibility? eligibility))
            {
                repo.SetEligibility(eligibility);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class SpyValidateTokenHandler(string accountName)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public bool WasCalled { get; private set; }

        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            ValidateToken.Response response = new(
                Kind: ValidateToken.Kinds.Authenticated,
                AccountName: accountName,
                MissingScopes: [],
                DetectedProvider: null);
            return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
        }
    }
}
