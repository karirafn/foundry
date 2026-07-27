using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Shared;
using Foundry.Testing;
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

    private UpdateAccount.Handler BuildHandler(
        IQueryHandler<ValidateToken.Query, ValidateToken.Response>? validateToken = null,
        INamespaceDeriver? deriver = null,
        IRepositoryEligibilityEvaluator? evaluator = null)
    {
        RepositoryEligibilityDiffer differ = new(
            _dbContext,
            evaluator ?? new NoOpEligibilityEvaluator());

        CredentialRotationService rotationService = new(
            _dbContext,
            deriver ?? new StubNamespaceDeriver(new NamespaceDerivationOutcome.Unavailable()),
            differ,
            NullLogger<CredentialRotationService>.Instance);

        return new UpdateAccount.Handler(
            _dbContext,
            validateToken ?? new StubValidateTokenHandler("updated-user"),
            rotationService);
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
        Result<CredentialUpdateResult> result = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        CredentialUpdateResult updateResult = result.ShouldBeOfType<Result<CredentialUpdateResult>.Success>().Value;
        updateResult.ShouldSatisfyAllConditions(
            () => updateResult.Credential.Id.ShouldBe(credential.Id.Value),
            () => updateResult.AffectedRepositories.ShouldBeEmpty());
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
        Result<CredentialUpdateResult> result = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        CredentialUpdateResult updateResult = result.ShouldBeOfType<Result<CredentialUpdateResult>.Success>().Value;
        updateResult.Credential.Name.ShouldBe("new-account-name");
    }

    [Fact]
    public async Task WhenNoTokenSupplied_SavesWithoutRotationAndReturnsEmptyAffected()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        UpdateAccount.Handler handler = BuildHandler();

        UpdateAccount.Command command = new(credential.Id, "https://github.com", Token: null);

        // Act
        Result<CredentialUpdateResult> result = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        CredentialUpdateResult updateResult = result.ShouldBeOfType<Result<CredentialUpdateResult>.Success>().Value;
        updateResult.ShouldSatisfyAllConditions(
            () => updateResult.Credential.Id.ShouldBe(credential.Id.Value),
            () => updateResult.AffectedRepositories.ShouldBeEmpty());
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

        // The deriver returns both namespaces; "shared-org" is already held by first
        UpdateAccount.Handler handler = BuildHandler(
            deriver: new StubNamespaceDeriver(new NamespaceDerivationOutcome.Derived([sharedNs, ownNs], [])));

        UpdateAccount.Command command = new(second.Id, "https://github.com", "ghp_newtoken");

        // Act
        Result<CredentialUpdateResult> result = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert — rotation succeeds; second only ends up with its own namespace
        Result<CredentialUpdateResult>.Success success = result.ShouldBeOfType<Result<CredentialUpdateResult>.Success>();
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == second.Id, CancellationToken.None);
        stored.ShouldNotBeNull();
        stored.Namespaces.ShouldContain(n => n.Value == "second-org");
        stored.Namespaces.ShouldNotContain(n => n.Value == "shared-org");
    }

    [Fact]
    public async Task WhenCredentialNotFound_ReturnsNotFoundError()
    {
        // Arrange
        UpdateAccount.Handler handler = BuildHandler();
        CredentialId nonExistentId = CredentialId.New();
        UpdateAccount.Command command = new(nonExistentId, "https://github.com", Token: null);

        // Act
        Result<CredentialUpdateResult> result = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<CredentialUpdateResult>.Failure failure = result.ShouldBeOfType<Result<CredentialUpdateResult>.Failure>();
        failure.Error.Code.ShouldBe(CredentialErrors.NotFoundCode);
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
                IsValid: true,
                IsAuthFailure: false,
                ScopesVerified: true,
                MissingScopes: [],
                AccountName: accountName);
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

    private sealed class AssignedEligibilityEvaluator(
        Dictionary<string, RepositoryEligibility> assignments) : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateAndStoreAsync(
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
}
