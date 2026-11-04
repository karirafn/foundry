using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.CreateRepositoryHandlerTests;

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

    private CreateRepository.Handler BuildHandler() =>
        new(
            _dbContext,
            new BranchRuleEnforcer(
                new NoOpEligibilityEvaluator(),
                new ThrowingProviderFactory(),
                NullLogger<BranchRuleEnforcer>.Instance),
            new StubGlobalSettingsQueries([]));

    private async Task<Guid> SeedCredentialAsync()
    {
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("octocat", "ghp_test", baseUrl);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return credential.Id.Value;
    }

    [Fact]
    public async Task WhenMaxConcurrentWorkersIsNull_DefaultsToOne()
    {
        // Arrange
        Guid accountId = await SeedCredentialAsync();
        CreateRepository.Handler sut = BuildHandler();
        CreateRepository.Command command = new(accountId, "owner/repo", PollIntervalSeconds: null, MaxConcurrentWorkers: null);

        // Act
        Result<CreateRepositoryResponse> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        RepositorySummary summary = ((Result<CreateRepositoryResponse>.Success)result).Value.Repository;
        summary.MaxConcurrentWorkers.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task WhenMaxConcurrentWorkersIsOutOfRange_ReturnsFailure(int maxConcurrentWorkers)
    {
        // Arrange
        Guid accountId = await SeedCredentialAsync();
        CreateRepository.Handler sut = BuildHandler();
        CreateRepository.Command command = new(accountId, "owner/repo", PollIntervalSeconds: null, MaxConcurrentWorkers: maxConcurrentWorkers);

        // Act
        Result<CreateRepositoryResponse> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        ((Result<CreateRepositoryResponse>.Failure)result).Error.Code.ShouldBe(MonitoredRepositoryErrors.InvalidMaxConcurrentWorkersCode);
    }

    [Fact]
    public async Task WhenMaxConcurrentWorkersIsProvided_RoundTripsInSummary()
    {
        // Arrange
        Guid accountId = await SeedCredentialAsync();
        CreateRepository.Handler sut = BuildHandler();
        CreateRepository.Command command = new(accountId, "owner/repo", PollIntervalSeconds: null, MaxConcurrentWorkers: 5);

        // Act
        Result<CreateRepositoryResponse> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        RepositorySummary summary = ((Result<CreateRepositoryResponse>.Success)result).Value.Repository;
        summary.MaxConcurrentWorkers.ShouldBe(5);
    }

    [Fact]
    public async Task WhenPollIntervalIsZero_ReturnsFailure()
    {
        // Arrange
        Guid accountId = await SeedCredentialAsync();
        CreateRepository.Handler sut = BuildHandler();
        CreateRepository.Command command = new(accountId, "owner/repo", PollIntervalSeconds: 0);

        // Act
        Result<CreateRepositoryResponse> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        ((Result<CreateRepositoryResponse>.Failure)result).Error.Code.ShouldBe(MonitoredRepositoryErrors.PollIntervalNotPositiveCode);
    }

    [Fact]
    public async Task WhenPollIntervalExceedsMaximum_ReturnsFailure()
    {
        // Arrange
        Guid accountId = await SeedCredentialAsync();
        CreateRepository.Handler sut = BuildHandler();
        CreateRepository.Command command = new(
            accountId,
            "owner/repo",
            PollIntervalSeconds: MonitoredRepository.MaxPollIntervalSeconds + 1);

        // Act
        Result<CreateRepositoryResponse> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        ((Result<CreateRepositoryResponse>.Failure)result).Error.Code.ShouldBe(MonitoredRepositoryErrors.PollIntervalTooLargeCode);
    }

    [Fact]
    public async Task WhenApplyBranchRulesIsOmitted_PersistsIntentAsTrue()
    {
        // Arrange
        Guid accountId = await SeedCredentialAsync();
        CreateRepository.Handler sut = BuildHandler();
        CreateRepository.Command command = new(accountId, "owner/repo", PollIntervalSeconds: null);

        // Act
        Result<CreateRepositoryResponse> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        MonitoredRepository stored = await _dbContext.Set<MonitoredRepository>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);
        stored.ApplyBranchRules.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenApplyBranchRulesIsFalse_PersistsIntentAndReturnsNotRequested()
    {
        // Arrange
        Guid accountId = await SeedCredentialAsync();
        CreateRepository.Handler sut = BuildHandler();
        CreateRepository.Command command = new(
            accountId,
            "owner/repo",
            PollIntervalSeconds: null,
            ApplyBranchRules: false);

        // Act
        Result<CreateRepositoryResponse> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        CreateRepositoryResponse response = ((Result<CreateRepositoryResponse>.Success)result).Value;
        response.BranchRules.Kind.ShouldBe(BranchRulesOutcome.NotRequestedKind);
        MonitoredRepository stored = await _dbContext.Set<MonitoredRepository>()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);
        stored.ApplyBranchRules.ShouldBeFalse();
    }

    private sealed class ThrowingProviderFactory : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) =>
            throw new InvalidOperationException("No provider call is expected.");
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
}
