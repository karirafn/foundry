using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.UpdateRepositoryHandlerTests;

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

    private UpdateRepository.Handler BuildHandler() => new(_dbContext);

    private async Task<(Guid AccountId, Guid RepositoryId)> SeedRepositoryAsync()
    {
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("octocat", "ghp_test", baseUrl);
        _dbContext.Set<Credential>().Add(credential);

        MonitoredRepository repo = new MonitoredRepositoryBuilder().Build();
        _dbContext.Set<MonitoredRepository>().Add(repo);

        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return (credential.Id.Value, repo.Id.Value);
    }

    [Fact]
    public async Task WhenMaxConcurrentWorkersIsUpdated_RoundTripsInSummary()
    {
        // Arrange
        (Guid accountId, Guid repositoryId) = await SeedRepositoryAsync();
        UpdateRepository.Handler sut = BuildHandler();
        UpdateRepository.Command command = new(
            accountId,
            repositoryId,
            PollIntervalSeconds: null,
            IsActive: true,
            MaxConcurrentWorkers: 3);

        // Act
        Result<RepositorySummary> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        RepositorySummary summary = ((Result<RepositorySummary>.Success)result).Value;
        summary.MaxConcurrentWorkers.ShouldBe(3);
    }

    [Fact]
    public async Task WhenPollIntervalIsZero_ReturnsFailure()
    {
        // Arrange
        (Guid accountId, Guid repositoryId) = await SeedRepositoryAsync();
        UpdateRepository.Handler sut = BuildHandler();
        UpdateRepository.Command command = new(
            accountId,
            repositoryId,
            PollIntervalSeconds: 0,
            IsActive: true,
            MaxConcurrentWorkers: 1);

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
        (Guid accountId, Guid repositoryId) = await SeedRepositoryAsync();
        UpdateRepository.Handler sut = BuildHandler();
        UpdateRepository.Command command = new(
            accountId,
            repositoryId,
            PollIntervalSeconds: MonitoredRepository.MaxPollIntervalSeconds + 1,
            IsActive: true,
            MaxConcurrentWorkers: 1);

        // Act
        Result<RepositorySummary> result = await sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        ((Result<RepositorySummary>.Failure)result).Error.Code.ShouldBe(MonitoredRepositoryErrors.PollIntervalTooLargeCode);
    }
}
