using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.CreateAccountHandlerTests;

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

    private CreateAccount.Handler BuildHandler(
        INamespaceDeriver namespaceDeriver,
        IRepositoryEligibilityEvaluator? evaluator = null)
    {
        RepositoryEligibilityDiffer differ = new(
            _dbContext,
            evaluator ?? new NoOpEligibilityEvaluator());

        return new CreateAccount.Handler(_dbContext, new StubValidateTokenHandler(), namespaceDeriver, differ);
    }

    [Fact]
    public async Task WhenDeriverReturnsDerived_SetsNamespacesOnCredential()
    {
        // Arrange
        Namespace ns = Namespace.Create("octocat").ValueOrThrow();
        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Derived([ns]);
        CreateAccount.Handler handler = BuildHandler(new StubNamespaceDeriver(outcome));
        CreateAccount.Command command = new("github", "https://github.com", "ghp_test");

        // Act
        CreateAccount.Outcome result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        CreateAccount.Outcome.Created created = result.ShouldBeOfType<CreateAccount.Outcome.Created>();
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(created.Value.Credential.Id),
                TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.Namespaces.Count.ShouldBe(1);
        stored.Namespaces.ShouldContain(n => n.Value == "octocat");
    }

    [Fact]
    public async Task WhenDeriverReturnsUnavailable_SetsEmptyNamespaces()
    {
        // Arrange
        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Unavailable();
        CreateAccount.Handler handler = BuildHandler(new StubNamespaceDeriver(outcome));
        CreateAccount.Command command = new("github", "https://github.com", "ghp_test");

        // Act
        CreateAccount.Outcome result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        CreateAccount.Outcome.Created created = result.ShouldBeOfType<CreateAccount.Outcome.Created>();
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(created.Value.Credential.Id),
                TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.Namespaces.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNamespaceAlreadyClaimed_AndNoTakeover_ReturnsConflict()
    {
        // Arrange — seed an existing credential that claims "octocat"
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential existing = GitHubCredential.Create("other-user", "ghp_other", baseUrl);
        existing.SetNamespaces([Namespace.Create("octocat").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(existing);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        Namespace ns = Namespace.Create("octocat").ValueOrThrow();
        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Derived([ns]);
        CreateAccount.Handler handler = BuildHandler(new StubNamespaceDeriver(outcome));
        CreateAccount.Command command = new("github", "https://github.com", "ghp_test", TakeoverNamespaces: null);

        // Act
        CreateAccount.Outcome result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        CreateAccount.Outcome.Conflict conflict = result.ShouldBeOfType<CreateAccount.Outcome.Conflict>();
        conflict.Conflicts.Conflicts.Count.ShouldBe(1);
        conflict.Conflicts.Conflicts[0].ShouldSatisfyAllConditions(
            () => conflict.Conflicts.Conflicts[0].Namespace.ShouldBe("octocat"),
            () => conflict.Conflicts.Conflicts[0].HolderCredentialId.ShouldBe(existing.Id.Value),
            () => conflict.Conflicts.Conflicts[0].HolderName.ShouldBe("other-user"));
    }

    [Fact]
    public async Task WhenTakeoverNamespaceOutsideDerivedSet_ReturnsInvalidTakeover()
    {
        // Arrange — derived set is only "octocat"; requesting takeover of "other-org"
        Namespace ns = Namespace.Create("octocat").ValueOrThrow();
        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Derived([ns]);
        CreateAccount.Handler handler = BuildHandler(new StubNamespaceDeriver(outcome));
        CreateAccount.Command command = new(
            "github",
            "https://github.com",
            "ghp_test",
            TakeoverNamespaces: ["other-org"]);

        // Act
        CreateAccount.Outcome result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        CreateAccount.Outcome.InvalidTakeover invalid = result.ShouldBeOfType<CreateAccount.Outcome.InvalidTakeover>();
        invalid.Invalid.InvalidNamespaces.ShouldContain("other-org");
    }

    [Fact]
    public async Task WhenTakeoverRequested_AndDerivationUnavailable_AllRequestedNamespacesAreInvalid()
    {
        // Arrange — derivation unavailable means empty derived set; all requested takeover namespaces invalid
        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Unavailable();
        CreateAccount.Handler handler = BuildHandler(new StubNamespaceDeriver(outcome));
        CreateAccount.Command command = new(
            "github",
            "https://github.com",
            "ghp_test",
            TakeoverNamespaces: ["org-a"]);

        // Act
        CreateAccount.Outcome result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        CreateAccount.Outcome.InvalidTakeover invalid = result.ShouldBeOfType<CreateAccount.Outcome.InvalidTakeover>();
        invalid.Invalid.InvalidNamespaces.ShouldContain("org-a");
    }

    [Fact]
    public async Task WhenValidTakeover_TransfersNamespaceOwnership()
    {
        // Arrange — seed existing credential that owns "octocat"
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential existingHolder = GitHubCredential.Create("holder-user", "ghp_holder", baseUrl);
        existingHolder.SetNamespaces([Namespace.Create("octocat").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(existingHolder);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        Namespace ns = Namespace.Create("octocat").ValueOrThrow();
        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Derived([ns]);
        CreateAccount.Handler handler = BuildHandler(new StubNamespaceDeriver(outcome));
        CreateAccount.Command command = new(
            "github",
            "https://github.com",
            "ghp_test",
            TakeoverNamespaces: ["octocat"]);

        // Act
        CreateAccount.Outcome result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert — new credential created, holder lost the namespace
        CreateAccount.Outcome.Created created = result.ShouldBeOfType<CreateAccount.Outcome.Created>();
        created.Value.Credential.Namespaces.ShouldContain("octocat");

        Credential? holder = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == existingHolder.Id, TestContext.Current.CancellationToken);
        holder.ShouldNotBeNull();
        holder.Namespaces.ShouldNotContain(n => n.Value == "octocat");
    }

    [Fact]
    public async Task WhenPartialTakeover_DerivedSetIncludesOtherHoldersNamespace_NewCredentialOnlyReceivesTakenOverNamespaces()
    {
        // Arrange — credential A holds ns-b and ns-x; user requests takeover of only ns-x;
        // derivation returns [ns-x, ns-b] (both are in the derived set).
        // The new credential must NOT steal ns-b (no takeover requested), so it only gets ns-x.
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential holderA = GitHubCredential.Create("holder-a", "ghp_holder_a", baseUrl);
        holderA.SetNamespaces([
            Namespace.Create("ns-b").ValueOrThrow(),
            Namespace.Create("ns-x").ValueOrThrow(),
        ]);
        _dbContext.Set<Credential>().Add(holderA);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Derived([
            Namespace.Create("ns-x").ValueOrThrow(),
            Namespace.Create("ns-b").ValueOrThrow(),
        ]);
        CreateAccount.Handler handler = BuildHandler(new StubNamespaceDeriver(outcome));
        CreateAccount.Command command = new(
            "github",
            "https://github.com",
            "ghp_test",
            TakeoverNamespaces: ["ns-x"]);

        // Act
        CreateAccount.Outcome result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert — new credential gets ns-x only; holder A retains ns-b
        CreateAccount.Outcome.Created created = result.ShouldBeOfType<CreateAccount.Outcome.Created>();
        created.Value.Credential.Namespaces.ShouldContain("ns-x");
        created.Value.Credential.Namespaces.ShouldNotContain("ns-b");

        Credential? holderAfter = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == holderA.Id, TestContext.Current.CancellationToken);
        holderAfter.ShouldNotBeNull();
        holderAfter.Namespaces.ShouldNotContain(n => n.Value == "ns-x");
        holderAfter.Namespaces.ShouldContain(n => n.Value == "ns-b");
    }

    [Fact]
    public async Task WhenConflictMeanwhileVanishes_ClaimsNormally()
    {
        // Arrange — no other credential claims the namespace; takeover lists it but it's not actually conflicted
        Namespace ns = Namespace.Create("octocat").ValueOrThrow();
        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Derived([ns]);
        CreateAccount.Handler handler = BuildHandler(new StubNamespaceDeriver(outcome));
        CreateAccount.Command command = new(
            "github",
            "https://github.com",
            "ghp_test",
            TakeoverNamespaces: ["octocat"]);

        // Act
        CreateAccount.Outcome result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert — no error, credential created normally
        result.ShouldBeOfType<CreateAccount.Outcome.Created>();
    }

    [Fact]
    public async Task WhenTakeoverTransfersPreviouslyEligibleRepo_PriorStatusIsEligible()
    {
        // Arrange — holder owns "octocat"; repo octocat/hello-world was eligible under the holder.
        // After takeover the evaluator marks it eligible again.
        // With correct prior-status snapshotting, prior == current == "eligible" → repo is NOT affected.
        // With the bug (empty priorStatus), prior == "unreachable" != "eligible" → repo IS wrongly affected.
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential holder = GitHubCredential.Create("holder-user", "ghp_holder", baseUrl);
        holder.SetNamespaces([Namespace.Create("octocat").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(holder);

        RepositorySlug slug = RepositorySlug.Create("octocat/hello-world").ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(slug, "github.com", pollInterval: null);
        repo.SetEligibility(new RepositoryEligibility.Eligible());
        _dbContext.Set<MonitoredRepository>().Add(repo);

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Derived(
            [Namespace.Create("octocat").ValueOrThrow()]);
        EligibleEvaluator evaluator = new();
        CreateAccount.Handler handler = BuildHandler(new StubNamespaceDeriver(outcome), evaluator);
        CreateAccount.Command command = new(
            "github",
            "https://github.com",
            "ghp_test",
            TakeoverNamespaces: ["octocat"]);

        // Act
        CreateAccount.Outcome result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert — no affected repos because eligible → eligible is not a status change
        CreateAccount.Outcome.Created created = result.ShouldBeOfType<CreateAccount.Outcome.Created>();
        created.Value.AffectedRepositories.ShouldBeEmpty();
    }

    private sealed class StubValidateTokenHandler
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken)
        {
            ValidateToken.Response response = new(
                IsValid: true,
                IsAuthFailure: false,
                ScopesVerified: true,
                MissingScopes: [],
                AccountName: "octocat");
            return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
        }
    }

    private sealed class StubNamespaceDeriver(NamespaceDerivationOutcome outcome) : INamespaceDeriver
    {
        public Task<NamespaceDerivationOutcome> DeriveAsync(
            Credential credential,
            CancellationToken cancellationToken) =>
            Task.FromResult(outcome);
    }

    private sealed class NoOpEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Marks every repo eligible — simulates a healthy credential that can reach all repos.</summary>
    private sealed class EligibleEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateAndStoreAsync(MonitoredRepository repo, CancellationToken cancellationToken)
        {
            repo.SetEligibility(new RepositoryEligibility.Eligible());
            return Task.CompletedTask;
        }
    }
}
