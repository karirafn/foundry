using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
        new(_dbContext, new NoOpEligibilityEvaluator());

    private async Task<Guid> SeedCredentialAsync()
    {
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("octocat", "ghp_test", baseUrl);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return credential.Id.Value;
    }

    [Fact]
    public async Task WhenPollIntervalIsZero_ReturnsFailure()
    {
        // Arrange
        Guid accountId = await SeedCredentialAsync();
        CreateRepository.Handler sut = BuildHandler();
        CreateRepository.Command command = new(accountId, "owner/repo", PollIntervalSeconds: 0);

        // Act
        Result<RepositorySummary> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        ((Result<RepositorySummary>.Failure)result).Error.Code.ShouldBe(MonitoredRepositoryErrors.PollIntervalNotPositiveCode);
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
        Result<RepositorySummary> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        ((Result<RepositorySummary>.Failure)result).Error.Code.ShouldBe(MonitoredRepositoryErrors.PollIntervalTooLargeCode);
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
