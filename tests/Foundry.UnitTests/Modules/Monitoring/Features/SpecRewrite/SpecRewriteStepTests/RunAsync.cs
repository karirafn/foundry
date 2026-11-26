using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.SpecRewrite;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.SpecRewrite.SpecRewriteStepTests;

/// <summary>
/// Tests for the spec-rewrite pass (ADR 0125): it keeps an <see cref="IssueWatch"/> on the spec of each issue with open
/// work waiting to start a run or on a person, and sends the issue back to triage once a rewrite settles.
/// </summary>
public sealed class RunAsync : IAsyncDisposable
{
    private const string OriginalBody = "## Acceptance Criteria\n- [ ] Retries three times.\n- [ ] Logs each retry.";
    private const string TickedBody = "## Acceptance Criteria\n- [x] Retries three times.\n- [ ] Logs each retry.";
    private const string RewrittenBody = "## Acceptance Criteria\n- [ ] Retries five times.\n- [ ] Logs each retry.";
    private const string TypoFixedBody = "## Acceptance Criteria\n- [ ] Retries three times.\n- [ ] Logs every retry.";

    private static readonly DateTimeOffset Start = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _dispatcher = new();
    private readonly ScriptedProvider _provider = new();
    private readonly MonitoredRepository _repository;

    public RunAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();

        _repository = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "github.com",
            null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(_repository);
        _dbContext.SaveChanges();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static SpecHash SpecOf(string body) => SpecHash.From(IssueBodyHash.OfSpec(body).Value);

    private SpecRewriteStep BuildSut() =>
        new(
            new SpecWatchedIssueQueries(_dbContext),
            _dbContext,
            _dispatcher,
            NullLogger<SpecRewriteStep>.Instance);

    private IssueBuilder Builder(int issueNumber) =>
        new IssueBuilder()
            .WithMonitoredRepositoryId(_repository.Id)
            .WithIssueNumber(issueNumber)
            .WithBranchName($"feat/{issueNumber}-add-retry")
            .WithPullRequestUrl($"https://github.com/owner/repo/pull/{issueNumber}");

    private void Seed(Issue issue, SpecHash? triagedSpecHash)
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.Entry(issue).Property(i => i.TriagedSpecHash).CurrentValue = triagedSpecHash;
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private void SeedReview(int issueNumber = 1, string? triagedBody = OriginalBody) =>
        Seed(Builder(issueNumber).Review(), triagedBody is null ? null : SpecOf(triagedBody));

    private void SeedWatch(IssueWatch watch)
    {
        _dbContext.Set<IssueWatch>().Add(watch);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private void RecordTriagedSpec(int issueNumber, string body)
    {
        Issue issue = _dbContext.Set<Issue>().Single(i => i.IssueNumber == issueNumber);
        _dbContext.Entry(issue).Property(i => i.TriagedSpecHash).CurrentValue = SpecOf(body);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private async Task RunStepAtAsync(DateTimeOffset at)
    {
        await BuildSut().RunAsync(_repository, _provider, at, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private IssueWatch? LoadWatch(int issueNumber = 1)
    {
        _dbContext.ChangeTracker.Clear();
        return _dbContext.Set<IssueWatch>().SingleOrDefault(watch => watch.IssueNumber == issueNumber);
    }

    [Fact]
    public async Task WhenTheSpecIsTheOneTriageJudged_StartsAWatchOnItWithNothingPending()
    {
        // Arrange
        SeedReview();

        // Act
        await RunStepAtAsync(Start);

        // Assert
        IssueWatch watch = LoadWatch().ShouldNotBeNull();
        watch.ShouldSatisfyAllConditions(
            () => watch.Wait.ShouldBe(WatchedWait.SpecChanged),
            () => watch.BaselineBodyHash.ShouldBe(IssueBodyHash.OfSpec(OriginalBody)),
            () => watch.PendingBodyChange.ShouldBeNull(),
            () => _dispatcher.Events.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenNoTriageRecordedASpec_TheFirstReadIsTheBaseline()
    {
        // Arrange
        SeedReview(triagedBody: null);
        _provider.IssueBody = RewrittenBody;

        // Act
        await RunStepAtAsync(Start);
        await RunStepAtAsync(Start.AddMinutes(5));

        // Assert
        LoadWatch().ShouldNotBeNull().ShouldSatisfyAllConditions(
            watch => watch.BaselineBodyHash.ShouldBe(IssueBodyHash.OfSpec(RewrittenBody)),
            watch => watch.PendingBodyChange.ShouldBeNull());
        _dispatcher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheSpecDiffersFromTheTriagedOne_RecordsItAsPendingWithoutSendingTheIssueBack()
    {
        // Arrange
        SeedReview();
        _provider.IssueBody = RewrittenBody;

        // Act
        await RunStepAtAsync(Start);

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingBodyChange
            .ShouldBe(new PendingBodyChange(IssueBodyHash.OfSpec(RewrittenBody), Start));
    }

    [Fact]
    public async Task WhenARewriteOutlastsTheQuietPeriod_RaisesItAndDeletesTheWatchInTheSameSave()
    {
        // Arrange
        SeedReview();
        await RunStepAtAsync(Start);
        _provider.IssueBody = RewrittenBody;
        await RunStepAtAsync(Start.AddMinutes(1));

        // Act
        await RunStepAtAsync(Start.AddMinutes(3));

        // Assert
        _dispatcher.Events.ShouldHaveSingleItem()
            .ShouldBe(new IssueSpecRewritten(_repository.Id, 1, Start.AddMinutes(1)));
        LoadWatch().ShouldBeNull();
    }

    [Fact]
    public async Task WhenARewriteReturnsToTheBaselineWithinTheQuietPeriod_RaisesNothing()
    {
        // Arrange
        SeedReview();
        await RunStepAtAsync(Start);
        _provider.IssueBody = RewrittenBody;
        await RunStepAtAsync(Start.AddMinutes(1));
        _provider.IssueBody = OriginalBody;
        await RunStepAtAsync(Start.AddMinutes(2));

        // Act
        await RunStepAtAsync(Start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingBodyChange.ShouldBeNull();
    }

    [Fact]
    public async Task WhenOnlyATaskListCheckboxIsTicked_RaisesNothing()
    {
        // Arrange
        SeedReview();
        await RunStepAtAsync(Start);
        _provider.IssueBody = TickedBody;
        await RunStepAtAsync(Start.AddMinutes(1));

        // Act
        await RunStepAtAsync(Start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingBodyChange.ShouldBeNull();
    }

    [Fact]
    public async Task WhenATypoIsFixed_RaisesTheRewrite()
    {
        // Arrange
        SeedReview();
        await RunStepAtAsync(Start);
        _provider.IssueBody = TypoFixedBody;
        await RunStepAtAsync(Start.AddMinutes(1));

        // Act
        await RunStepAtAsync(Start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldHaveSingleItem().ShouldBeOfType<IssueSpecRewritten>();
    }

    [Fact]
    public async Task WhenTheBodyReadFails_LeavesTheWatchAndRaisesNothing()
    {
        // Arrange
        SeedReview();
        await RunStepAtAsync(Start);
        _provider.IssueBody = RewrittenBody;
        await RunStepAtAsync(Start.AddMinutes(1));
        _provider.IssueStatusFails = true;

        // Act
        await RunStepAtAsync(Start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingBodyChange
            .ShouldBe(new PendingBodyChange(IssueBodyHash.OfSpec(RewrittenBody), Start.AddMinutes(1)));
    }

    [Fact]
    public async Task WhenTheFirstBodyReadFails_StartsNoWatch()
    {
        // Arrange
        SeedReview();
        _provider.IssueStatusFails = true;

        // Act
        await RunStepAtAsync(Start);

        // Assert
        LoadWatch().ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheIssueIsClosed_ComparesNothing()
    {
        // Arrange
        SeedReview();
        await RunStepAtAsync(Start);
        _provider.IssueBody = RewrittenBody;
        _provider.IssueClosed = true;

        // Act
        await RunStepAtAsync(Start.AddMinutes(5));

        // Assert
        LoadWatch().ShouldNotBeNull().PendingBodyChange.ShouldBeNull();
        _dispatcher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheNextTriageRecordedTheRewrittenSpec_TheSameRewriteNeverFiresAgain()
    {
        // Arrange
        SeedReview();
        SeedWatch(IssueWatch.StartForSpecChange(_repository.Id, 1, IssueBodyHash.OfSpec(OriginalBody)));
        _provider.IssueBody = RewrittenBody;
        RecordTriagedSpec(1, RewrittenBody);

        // Act
        await RunStepAtAsync(Start);
        await RunStepAtAsync(Start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().BaselineBodyHash.ShouldBe(IssueBodyHash.OfSpec(RewrittenBody));
    }

    [Theory]
    [InlineData("revision_queued")]
    [InlineData("revision_blocked")]
    [InlineData("continuation_queued")]
    [InlineData("continuation_blocked")]
    [InlineData("review")]
    [InlineData("revision_failed")]
    public async Task WhenTheIssueWaitsInAWatchedState_ARewriteSendsItBack(string state)
    {
        // Arrange
        IssueBuilder builder = Builder(1);
        Issue issue = state switch
        {
            "revision_queued" => builder.RevisionQueued(),
            "revision_blocked" => builder.RevisionBlocked([5]),
            "continuation_queued" => builder.ContinuationQueued(),
            "continuation_blocked" => builder.ContinuationBlocked([5]),
            "review" => builder.Review(),
            _ => builder.RevisionFailed(),
        };
        Seed(issue, SpecOf(OriginalBody));
        _provider.IssueBody = RewrittenBody;
        await RunStepAtAsync(Start);

        // Act
        await RunStepAtAsync(Start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldHaveSingleItem().ShouldBe(new IssueSpecRewritten(_repository.Id, 1, Start));
    }

    [Theory]
    [InlineData("revision_in_progress")]
    [InlineData("rebase_pending")]
    [InlineData("checks_pending")]
    [InlineData("review_queued")]
    [InlineData("review_in_progress")]
    [InlineData("merge_pending")]
    [InlineData("awaiting_input")]
    public async Task WhenARunIsInProgressOrTheCodeOwnedPipelineRuns_ReadsNothingAndStartsNoWatch(string state)
    {
        // Arrange
        IssueBuilder builder = Builder(1);
        Issue issue = state switch
        {
            "revision_in_progress" => builder.RevisionInProgress(),
            "rebase_pending" => builder.RebasePending(),
            "checks_pending" => builder.ChecksPending(),
            "review_queued" => builder.ReviewQueued(),
            "review_in_progress" => builder.ReviewInProgress(),
            "merge_pending" => builder.MergePending(),
            _ => builder.AwaitingInput(),
        };
        Seed(issue, SpecOf(OriginalBody));
        _provider.IssueBody = RewrittenBody;

        // Act
        await RunStepAtAsync(Start);
        await RunStepAtAsync(Start.AddMinutes(5));

        // Assert
        _provider.IssueStatusReads.ShouldBe(0);
        LoadWatch().ShouldBeNull();
        _dispatcher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheSpecIsRewrittenWhileChecksArePending_ItFiresOnceTheIssueReachesReview()
    {
        // Arrange
        Seed(Builder(1).ChecksPending(), SpecOf(OriginalBody));
        _provider.IssueBody = RewrittenBody;
        await RunStepAtAsync(Start);
        _dispatcher.Events.ShouldBeEmpty();

        ChecksPendingIssue checksPending = _dbContext.Set<Issue>().OfType<ChecksPendingIssue>().Single();
        ReviewIssue review = checksPending.ToReview(new ChecksOutcome.Passed(), verifiedHeadSha: null, Start.AddMinutes(10));
        await _dbContext.TransitionAsync(checksPending, review, new CapturingDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        await RunStepAtAsync(Start.AddMinutes(11));

        // Act
        await RunStepAtAsync(Start.AddMinutes(14));

        // Assert
        _dispatcher.Events.ShouldHaveSingleItem()
            .ShouldBe(new IssueSpecRewritten(_repository.Id, 1, Start.AddMinutes(11)));
    }

    [Fact]
    public async Task WhenSeveralIssuesAreWatched_ReadsEachOncePerPoll()
    {
        // Arrange
        SeedReview(issueNumber: 1);
        SeedReview(issueNumber: 2);
        Seed(Builder(3).ChecksPending(), SpecOf(OriginalBody));

        // Act
        await RunStepAtAsync(Start);

        // Assert
        _provider.IssueStatusReads.ShouldBe(2);
    }

    [Fact]
    public async Task WhenAnIssueLeftTheWatchedStates_DeletesItsWatch()
    {
        // Arrange
        SeedWatch(IssueWatch.StartForSpecChange(_repository.Id, 7, IssueBodyHash.OfSpec(OriginalBody)));

        // Act
        await RunStepAtAsync(Start);

        // Assert
        LoadWatch(7).ShouldBeNull();
    }

    [Fact]
    public async Task WhenAnotherWaitsWatchBelongsToAnUnwatchedIssue_KeepsIt()
    {
        // Arrange
        SeedWatch(IssueWatch.StartForFailedTriage(_repository.Id, 7, IssueBodyHash.Of("Failed.")));

        // Act
        await RunStepAtAsync(Start);

        // Assert
        LoadWatch(7).ShouldNotBeNull().Wait.ShouldBe(WatchedWait.TriageFailed);
    }

    [Fact]
    public async Task WhenAWatchedIssueHasAWatchLeftFromAnotherWait_ReplacesItWithTheTriagedSpecAsBaseline()
    {
        // Arrange
        SeedReview();
        SeedWatch(IssueWatch.Start(_repository.Id, 1, IssueBodyHash.Of("Waiting."), baselineHead: null));

        // Act
        await RunStepAtAsync(Start);

        // Assert
        IssueWatch watch = LoadWatch().ShouldNotBeNull();
        watch.ShouldSatisfyAllConditions(
            () => watch.Wait.ShouldBe(WatchedWait.SpecChanged),
            () => watch.BaselineBodyHash.ShouldBe(IssueBodyHash.OfSpec(OriginalBody)));
    }

    private sealed class ScriptedProvider : IIssueProvider
    {
        public bool IssueClosed { get; set; }

        public string IssueBody { get; set; } = OriginalBody;

        public bool IssueStatusFails { get; set; }

        public int IssueStatusReads { get; private set; }

        public Task<Result<IssueStatus>> GetIssueStatusAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken)
        {
            IssueStatusReads++;
            return Task.FromResult(IssueStatusFails
                ? Result<IssueStatus>.Fail(new Error("Test.Unavailable", "503"))
                : Result<IssueStatus>.Ok(new IssueStatus(IssueClosed, IssueBody)));
        }

        public Task<Result<bool>> IsIssueClosedAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<ProviderCommentId>> PostCommentAsync(RepositorySlug slug, CommentThread thread, string body, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<ProviderComment>>> GetIssueFeedbackAsync(
            RepositorySlug slug,
            int issueNumber,
            DateTimeOffset since,
            PostedCommentIds postedComments,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            PostedCommentIds postedComments,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<IssueListing>> GetIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result> ApplyBranchRulesAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<bool>> CreateBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<bool>> DeleteBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(RepositorySlug slug, string branchName, CommitSha? publishedHead, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<Checks>> GetChecksAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            Events.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
