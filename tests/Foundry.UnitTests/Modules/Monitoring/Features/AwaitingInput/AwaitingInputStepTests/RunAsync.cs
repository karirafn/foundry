using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.AwaitingInput;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.AwaitingInput;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.AwaitingInput.AwaitingInputStepTests;

/// <summary>
/// Tests for the awaiting-input step (ADR 0096): posting the questions, reading input on the question's thread, and
/// the review pass's ways out.
/// </summary>
public sealed class RunAsync : IAsyncDisposable
{
    private const string PrUrl = "https://github.com/owner/repo/pull/7";
    private const string IssueUrl = "https://github.com/owner/repo/issues/1";

    private static readonly DateTimeOffset PostedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _dispatcher = new();
    private readonly WebhookNudgeQueue _nudgeQueue = new(NullLogger<WebhookNudgeQueue>.Instance);
    private readonly FakeTimeProvider _timeProvider = new(PostedAt.AddHours(1));
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

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    private AwaitingInputStep BuildSut() =>
        new(
            new IssueQueries(
                _dbContext,
                new NullRepositorySlugQueries(),
                new NullRepositoryEligibilityQuery(),
                new NullWorkerRunQueries(),
                new InFlightWorkerCountQuery(_dbContext),
                new NullGlobalSettingsQueries()),
            new AwaitingInputCommands(_dbContext, NullLogger<AwaitingInputCommands>.Instance),
            _dbContext,
            _dispatcher,
            new PostedCommentRegistry(_dbContext),
            new ActionableFeedbackPolicy(_timeProvider),
            _nudgeQueue,
            NullLogger<AwaitingInputStep>.Instance);

    private IssueBuilder Builder() =>
        new IssueBuilder()
            .WithMonitoredRepositoryId(_repository.Id)
            .WithIssueNumber(1)
            .WithPullRequestUrl(PrUrl);

    private void Seed(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private void SeedPostedRegistryEntry(long commentId)
    {
        _dbContext.Set<PostedComment>().Add(
            PostedComment.Record(_repository.Id, ProviderCommentId.Create(commentId).ValueOrThrow(), PostedAt));
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private Task RunStepAsync() =>
        BuildSut().RunAsync(_repository, _provider, issueNumber: null, Now, TestContext.Current.CancellationToken);

    private AwaitingInputIssue LoadAwaiting()
    {
        _dbContext.ChangeTracker.Clear();
        return _dbContext.Set<Issue>().Single().ShouldBeOfType<AwaitingInputIssue>();
    }

    [Fact]
    public async Task WhenPendingWithAPullRequest_PostsOnThePullRequestAndRecordsTheCommentInTheSameSave()
    {
        // Arrange
        Seed(Builder().AwaitingInput());
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(555).ValueOrThrow());

        // Act
        await RunStepAsync();

        // Assert
        AwaitingInputIssue awaiting = LoadAwaiting();
        QuestionPost.Posted posted = awaiting.QuestionPost.ShouldBeOfType<QuestionPost.Posted>();
        posted.ShouldSatisfyAllConditions(
            () => _provider.PostedThreads.ShouldHaveSingleItem().ShouldBe(new CommentThread.PullRequest(PrUrl)),
            () => _provider.PostedBodies.ShouldHaveSingleItem().ShouldContain("Which database?"),
            () => posted.CommentId.ShouldBe(555),
            () => posted.PostedAt.ShouldBe(Now),
            () => posted.CommentUrl.ShouldBe($"{PrUrl}#issuecomment-555"),
            () => _dbContext.Set<PostedComment>().ShouldHaveSingleItem().CommentId.Value.ShouldBe(555));
    }

    [Fact]
    public async Task WhenPendingWithoutAPullRequest_PostsOnTheIssue()
    {
        // Arrange
        Seed(Builder().AwaitingInputWithoutPullRequest(branchHasCommits: false));
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(556).ValueOrThrow());

        // Act
        await RunStepAsync();

        // Assert
        _provider.PostedThreads.ShouldHaveSingleItem().ShouldBe(new CommentThread.Issue(1));
        LoadAwaiting().QuestionPost.ShouldBeOfType<QuestionPost.Posted>().CommentUrl.ShouldBe($"{IssueUrl}#issuecomment-556");
    }

    [Fact]
    public async Task WhenAPostFailsTransiently_RecordsARetryAndRegistersNothing()
    {
        // Arrange
        Seed(Builder().AwaitingInput());
        _provider.PostResult = Result<ProviderCommentId>.Fail(CommentPostErrors.Transient("503"));

        // Act
        await RunStepAsync();

        // Assert
        QuestionPost.Failed failed = LoadAwaiting().QuestionPost.ShouldBeOfType<QuestionPost.Failed>();
        failed.ShouldSatisfyAllConditions(
            () => failed.NextAttemptAt.ShouldBe(Now + QuestionPost.FirstRetryDelay),
            () => _dbContext.Set<PostedComment>().ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenARetryIsNotDue_DoesNotPost()
    {
        // Arrange
        AwaitingInputIssue awaiting = Builder().AwaitingInput();
        awaiting.RecordQuestionPostFailed("503", transient: true, Now);
        Seed(awaiting);

        // Act
        await RunStepAsync();

        // Assert
        _provider.PostedThreads.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAnEligibleReplyOutlastsTheQuietPeriodOnTheIssue_InputArrivesSkippingFoundrysOwnComment()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedPostedRegistryEntry(1001);
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(1001).WithAuthorLogin("operator").WithBody("Foundry needs input").WithCreatedAt(PostedAt.AddSeconds(1)).Build(),
            new ProviderCommentBuilder().WithId(2002).WithAuthorLogin("operator").WithBody("Use Postgres.").WithCreatedAt(PostedAt.AddMinutes(5)).Build(),
        ];

        // Act
        await RunStepAsync();

        // Assert
        AwaitingInputArrived arrived = _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem();
        arrived.ShouldSatisfyAllConditions(
            () => arrived.IssueNumber.ShouldBe(1),
            () => arrived.Input.ShouldBe("Use Postgres."),
            () => arrived.ArrivedAt.ShouldBe(PostedAt.AddMinutes(5)),
            () => _provider.IssueFeedbackSince.ShouldBe(PostedAt));
    }

    [Fact]
    public async Task WhenAnEligibleReplyIsLong_InputArrivesWhole()
    {
        // Arrange
        string longReply = new('x', 4100);
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(2002).WithAuthorLogin("operator").WithBody(longReply).WithCreatedAt(PostedAt.AddMinutes(5)).Build(),
        ];

        // Act
        await RunStepAsync();

        // Assert
        _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem().Input.ShouldBe(longReply);
    }

    [Fact]
    public async Task WhenAReplyIsStillInsideTheQuietPeriod_NoInputArrivesAndAFollowUpIsNudged()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(2002).WithBody("Use Postgres.").WithCreatedAt(Now.AddSeconds(-30)).Build(),
        ];

        // Act
        await RunStepAsync();

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        _nudgeQueue.Reader.TryRead(out Nudge nudge).ShouldBeTrue();
        nudge.Key.Pass.ShouldBe(NudgePass.AwaitingInput);
    }

    [Fact]
    public async Task WhenOnlyABotReplied_NoInputArrives()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(2002).WithAuthorIsBot(true).WithCreatedAt(PostedAt.AddMinutes(5)).Build(),
        ];

        // Act
        await RunStepAsync();

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPostedOnAPullRequest_ReadsItsThreadAfterThePostTime()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt));
        _provider.ReviewFeedback = new ReviewFeedback([new ReviewComment("Use Postgres.")], 0, PostedAt.AddMinutes(5));

        // Act
        await RunStepAsync();

        // Assert
        _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem().Input.ShouldBe("Use Postgres.");
        _provider.ReviewFeedbackSince.ShouldBe(PostedAt);
    }

    [Fact]
    public async Task WhenTheIssueClosed_LeavesWithoutPosting()
    {
        // Arrange
        Seed(Builder().AwaitingInput());
        _provider.IssueClosed = true;

        // Act
        await RunStepAsync();

        // Assert
        _dispatcher.Events.ShouldHaveSingleItem().ShouldBeOfType<ProviderIssueClosed>();
        _provider.PostedThreads.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenThePullRequestClosedWithoutMerging_Leaves()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt));
        _provider.PullRequestStatus = new PullRequestStatus(IsClosed: true, IsMerged: false, HeadSha: null);

        // Act
        await RunStepAsync();

        // Assert
        _dispatcher.Events.ShouldHaveSingleItem().ShouldBeOfType<ProviderPullRequestClosed>();
    }

    private sealed class ScriptedProvider : IIssueProvider
    {
        public bool IssueClosed { get; set; }

        public PullRequestStatus PullRequestStatus { get; set; } = new(IsClosed: false, IsMerged: false, HeadSha: null);

        public Result<ProviderCommentId> PostResult { get; set; } =
            Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent("not scripted"));

        public List<CommentThread> PostedThreads { get; } = [];

        public List<string> PostedBodies { get; } = [];

        public IReadOnlyList<ProviderComment> IssueComments { get; set; } = [];

        public DateTimeOffset? IssueFeedbackSince { get; private set; }

        public ReviewFeedback ReviewFeedback { get; set; } = new([], 0, null);

        public DateTimeOffset? ReviewFeedbackSince { get; private set; }

        public Task<Result<bool>> IsIssueClosedAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(IssueClosed));

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<PullRequestStatus>.Ok(PullRequestStatus));

        public Task<Result<ProviderCommentId>> PostCommentAsync(
            RepositorySlug slug,
            CommentThread thread,
            string body,
            CancellationToken cancellationToken)
        {
            PostedThreads.Add(thread);
            PostedBodies.Add(body);
            return Task.FromResult(PostResult);
        }

        public Task<Result<IReadOnlyList<ProviderComment>>> GetIssueFeedbackAsync(
            RepositorySlug slug,
            int issueNumber,
            DateTimeOffset since,
            PostedCommentIds postedComments,
            CancellationToken cancellationToken)
        {
            IssueFeedbackSince = since;
            return Task.FromResult(Result<IReadOnlyList<ProviderComment>>.Ok(IssueComments));
        }

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            PostedCommentIds postedComments,
            CancellationToken cancellationToken)
        {
            ReviewFeedbackSince = since;
            return Task.FromResult(Result<ReviewFeedback>.Ok(ReviewFeedback));
        }

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

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
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
