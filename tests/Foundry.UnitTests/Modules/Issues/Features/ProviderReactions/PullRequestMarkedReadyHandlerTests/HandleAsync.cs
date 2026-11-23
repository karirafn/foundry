using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.ProviderReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.ProviderReactions.PullRequestMarkedReadyHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const int IssueNumber = 7;
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/8";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<PullRequestMarkedReady> _sut;
    private readonly MonitoredRepositoryId _repositoryId = MonitoredRepositoryId.New();

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new PullRequestMarkedReadyHandler(_dbContext);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IssueBuilder Builder() => new IssueBuilder()
        .WithMonitoredRepositoryId(_repositoryId)
        .WithIssueNumber(IssueNumber)
        .WithPullRequestUrl(PullRequestUrl);

    private async Task SeedAsync(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<Issue> ReloadAsync()
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .SingleAsync(i => i.MonitoredRepositoryId == _repositoryId, TestContext.Current.CancellationToken);
    }

    private PullRequestMarkedReady Event(string pullRequestUrl = PullRequestUrl) =>
        new(_repositoryId, IssueNumber, pullRequestUrl);

    [Fact]
    public async Task WhenReviewIssueHoldsThePullRequest_RecordsItMarkedReady()
    {
        // Arrange
        await SeedAsync(Builder().Review());

        // Act
        await _sut.HandleAsync(Event(), TestContext.Current.CancellationToken);

        // Assert
        Issue issue = await ReloadAsync();
        issue.MarkedReadyPullRequestUrl.ShouldBe(PullRequestUrl);
        issue.IsPullRequestMarkedReady.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenMergePendingIssueHoldsThePullRequest_RecordsItMarkedReady()
    {
        // Arrange
        await SeedAsync(Builder().MergePending());

        // Act
        await _sut.HandleAsync(Event(), TestContext.Current.CancellationToken);

        // Assert
        Issue issue = await ReloadAsync();
        issue.ShouldBeOfType<MergePendingIssue>().IsPullRequestMarkedReady.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenRedelivered_LeavesTheIssueMarked()
    {
        // Arrange
        await SeedAsync(Builder().Review());
        await _sut.HandleAsync(Event(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        await _sut.HandleAsync(Event(), TestContext.Current.CancellationToken);

        // Assert
        Issue issue = await ReloadAsync();
        issue.MarkedReadyPullRequestUrl.ShouldBe(PullRequestUrl);
        issue.IsPullRequestMarkedReady.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenIssueHoldsNoPullRequest_RecordsNothing()
    {
        // Arrange
        await SeedAsync(Builder().FreshQueued());

        // Act
        await _sut.HandleAsync(Event(), TestContext.Current.CancellationToken);

        // Assert
        Issue issue = await ReloadAsync();
        issue.MarkedReadyPullRequestUrl.ShouldBeNull();
    }

    [Fact]
    public async Task WhenEventNamesAnEarlierPullRequest_RecordsNothing()
    {
        // Arrange
        await SeedAsync(Builder().Review());

        // Act
        await _sut.HandleAsync(Event("https://github.com/owner/repo/pull/3"), TestContext.Current.CancellationToken);

        // Assert
        Issue issue = await ReloadAsync();
        issue.MarkedReadyPullRequestUrl.ShouldBeNull();
    }

    [Fact]
    public async Task WhenIssueDoesNotExist_DoesNothing()
    {
        // Act
        await _sut.HandleAsync(Event(), TestContext.Current.CancellationToken);

        // Assert
        (await _dbContext.Set<Issue>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }
}
