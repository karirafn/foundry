using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Issues.Features.TriageActionHandlerTests;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.RetryReviewHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string ReviewedCommit = "0123456789abcdef0123456789abcdef01234567";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher = new();
    private readonly StubIssueQueries _issueQueries = new();

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

    private async Task<TIssue> SeedAsync<TIssue>(TIssue issue)
        where TIssue : Issue
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private async Task<Issue> ReloadAsync(Issue issue)
    {
        _dbContext.ChangeTracker.Clear();
        Issue? reloaded = await _dbContext.Set<Issue>().FindAsync([issue.Id], TestContext.Current.CancellationToken);
        return reloaded.ShouldNotBeNull();
    }

    private RetryReview.Handler CreateSut() => new(_dbContext, _dispatcher, TimeProvider.System, _issueQueries);

    [Fact]
    public async Task WhenTheReviewWasSkipped_QueuesTheReviewOfTheSameCommitWithItsAutoMerge()
    {
        // Arrange
        ReviewIssue skipped = await SeedAsync(new IssueBuilder()
            .WithVerifiedHeadSha(ReviewedCommit)
            .WithAutoMerge(true)
            .ReviewSkipped());

        // Act
        Result<IssueDetail> result = await CreateSut().HandleAsync(
            new RetryReview.Command(skipped.Id),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        ReviewQueuedIssue queued = (await ReloadAsync(skipped)).ShouldBeOfType<ReviewQueuedIssue>();
        queued.ShouldSatisfyAllConditions(
            () => queued.ReviewedCommit.ShouldBe(ReviewedCommit),
            () => queued.AutoMerge.ShouldBeTrue(),
            () => _dispatcher.DispatchedEvents.OfType<IssueReviewQueued>().ShouldHaveSingleItem());
    }

    [Fact]
    public async Task WhenTheReviewWasNotSkipped_IsAWrongStateError()
    {
        // Arrange
        ReviewIssue review = await SeedAsync(new IssueBuilder().Review());

        // Act
        Result<IssueDetail> result = await CreateSut().HandleAsync(
            new RetryReview.Command(review.Id),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.WrongStateCode);
        (await ReloadAsync(review)).ShouldBeOfType<ReviewIssue>();
    }

    [Fact]
    public async Task WhenTheIssueLeftReview_IsAWrongStateError()
    {
        // Arrange
        ReviewQueuedIssue queued = await SeedAsync(new IssueBuilder().ReviewQueued());

        // Act
        Result<IssueDetail> result = await CreateSut().HandleAsync(
            new RetryReview.Command(queued.Id),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.WrongStateCode);
    }

    [Fact]
    public async Task WhenTheIssueDoesNotExist_IsANotFoundError()
    {
        // Act
        Result<IssueDetail> result = await CreateSut().HandleAsync(
            new RetryReview.Command(IssueId.New()),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.NotFoundCode);
    }
}
