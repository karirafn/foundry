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
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;
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
/// Tests for the awaiting-input step (ADR 0096): posting the questions, reading input on the question's thread and,
/// on an issue-thread wait, the issue body through its Issue Watch, and the review pass's ways out.
/// </summary>
public sealed class RunAsync : IAsyncDisposable
{
    private const string PrUrl = "https://github.com/owner/repo/pull/7";
    private const string IssueUrl = "https://github.com/owner/repo/issues/1";
    private const string BranchName = "feat/1-test";
    private const string PostedHead = "aaa111";
    private const string PushedHead = "bbb222";

    private static readonly DateTimeOffset PostedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _dispatcher = new();
    private readonly WebhookNudgeQueue _nudgeQueue = new(NullLogger<WebhookNudgeQueue>.Instance);
    private readonly MutableTimeProvider _timeProvider = new(PostedAt.AddHours(1));
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
                new InFlightWorkerCountQuery(_dbContext, new NullWorkerRunQueries()),
                new NullGlobalSettingsQueries(),
                new NullMergeQueueQueries(),
                new NullRepositoryLifecycleSettingsQueries()),
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
            PostedComment.Record(_repository.Id, PostedCommentKey.Conversation(ProviderCommentId.Create(commentId).ValueOrThrow()), PostedAt));
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private Task RunStepAsync() =>
        BuildSut().RunAsync(_repository, _provider, issueNumber: null, Now, TestContext.Current.CancellationToken);

    private async Task RunStepAtAsync(DateTimeOffset at)
    {
        _timeProvider.Now = at;
        await RunStepAsync();
        _dbContext.ChangeTracker.Clear();
    }

    private void SeedWatch(int issueNumber, string? baselineBody, BranchHead? baselineHead = null)
    {
        _dbContext.Set<IssueWatch>().Add(IssueWatch.Start(
            _repository.Id,
            issueNumber,
            baselineBody is null ? null : IssueBodyHash.Of(baselineBody),
            baselineHead));
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private void SeedFailedTriageWatch(int issueNumber, string baselineBody)
    {
        _dbContext.Set<IssueWatch>().Add(IssueWatch.StartForFailedTriage(_repository.Id, issueNumber, IssueBodyHash.Of(baselineBody)));
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private IssueWatch? LoadWatch()
    {
        _dbContext.ChangeTracker.Clear();
        return _dbContext.Set<IssueWatch>().SingleOrDefault();
    }

    private Nudge? ReadNudge() => _nudgeQueue.Reader.TryRead(out Nudge nudge) ? nudge : null;

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
    public async Task WhenTriageDecidedDetailsBesideItsQuestions_PostsTheDecisionsInTheQuestionComment()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder()
            .WithMonitoredRepositoryId(_repository.Id)
            .WithIssueNumber(1)
            .TriageInProgress()
            .MarkNeedsInput(
                new TriageVerdict.NeedsInput(IssueKind.Feature, [StructuredQuestionBuilder.Build()], PostedAt),
                [TriageDecisionBuilder.Build(question: "Which retry cap?", decision: "Three attempts.")],
                PostedAt);
        Seed(awaiting);
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(557).ValueOrThrow());

        // Act
        await RunStepAsync();

        // Assert
        _provider.PostedBodies.ShouldHaveSingleItem().ShouldContain(
            "Triage also decided:\n- `Which retry cap?`: `Three attempts.` (Settled by existing behaviour)");
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
            () => arrived.Input.ShouldBe(ClarificationInput.FromComments("Use Postgres.")),
            () => arrived.ArrivedAt.ShouldBe(PostedAt.AddMinutes(5)),
            () => _provider.IssueFeedbackSince.ShouldBe(PostedAt));
    }

    [Fact]
    public async Task WhenSeveralRepliesOutlastTheQuietPeriod_InputArrivesAsOneCommentsPartJoinedOldestFirst()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(2002).WithAuthorLogin("operator").WithBody("Use Postgres. ").WithCreatedAt(PostedAt.AddMinutes(5)).Build(),
            new ProviderCommentBuilder().WithId(2003).WithAuthorLogin("operator").WithBody("And keep SQLite for tests.").WithCreatedAt(PostedAt.AddMinutes(6)).Build(),
        ];

        // Act
        await RunStepAsync();

        // Assert
        _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem().Input.Parts.ShouldHaveSingleItem()
            .ShouldBe(new InputPart.Comments("Use Postgres.\n\nAnd keep SQLite for tests."));
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
        _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem().Input.ShouldBe(ClarificationInput.FromComments(longReply));
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
        _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem().Input.ShouldBe(ClarificationInput.FromComments("Use Postgres."));
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
        _provider.PullRequestStatus = new PullRequestStatus(IsClosed: true, IsMerged: false, HeadSha: null, IsDraft: false);

        // Act
        await RunStepAsync();

        // Assert
        _dispatcher.Events.ShouldHaveSingleItem().ShouldBeOfType<ProviderPullRequestClosed>();
    }

    [Fact]
    public async Task WhenAWaitIsChecked_ReadsTheIssueStatusOncePerIssue()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt));

        // Act
        await RunStepAsync();

        // Assert
        _provider.IssueStatusReads.ShouldBe(1);
    }

    [Fact]
    public async Task WhenPostedOnTheIssue_StartsAWatchWithTheBodyReadInThatPass()
    {
        // Arrange
        Seed(Builder().AwaitingInputWithoutPullRequest(branchHasCommits: false));
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(556).ValueOrThrow());

        // Act
        await RunStepAsync();

        // Assert
        IssueWatch watch = LoadWatch().ShouldNotBeNull();
        watch.ShouldSatisfyAllConditions(
            () => watch.IssueNumber.ShouldBe(1),
            () => watch.BaselineBodyHash.ShouldBe(IssueBodyHash.Of(_provider.IssueBody)),
            () => watch.PendingBodyChange.ShouldBeNull());
    }

    [Fact]
    public async Task WhenPostedOnTheIssue_ReplacesAWatchLeftFromAnEarlierRound()
    {
        // Arrange
        Seed(Builder().AwaitingInputWithoutPullRequest(branchHasCommits: false));
        SeedWatch(issueNumber: 1, baselineBody: "An older description.");
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(556).ValueOrThrow());

        // Act
        await RunStepAsync();

        // Assert
        LoadWatch().ShouldNotBeNull().BaselineBodyHash.ShouldBe(IssueBodyHash.Of(_provider.IssueBody));
    }

    [Fact]
    public async Task WhenTheBodyReadFailedAtPost_PostsWithoutABaselineAndTheNextReadRecordsItWithoutInput()
    {
        // Arrange
        Seed(Builder().AwaitingInputWithoutPullRequest(branchHasCommits: false));
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(556).ValueOrThrow());
        _provider.IssueStatusFails = true;
        await RunStepAtAsync(Now);
        LoadWatch().ShouldNotBeNull().BaselineBodyHash.ShouldBeNull();
        _provider.IssueStatusFails = false;
        _provider.IssueBody = "Edited before the read recovered.";

        // Act
        await RunStepAtAsync(Now.AddMinutes(5));

        // Assert
        IssueWatch watch = LoadWatch().ShouldNotBeNull();
        watch.BaselineBodyHash.ShouldBe(IssueBodyHash.Of("Edited before the read recovered."));
        watch.PendingBodyChange.ShouldBeNull();
        _dispatcher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheBodyChanges_RecordsItAsPendingAndNudgesWhenItSettles()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody);
        _provider.IssueBody = "The fixed requirements.";

        // Act
        await RunStepAtAsync(Now);

        // Assert
        LoadWatch().ShouldNotBeNull().PendingBodyChange
            .ShouldBe(new PendingBodyChange(IssueBodyHash.Of("The fixed requirements."), Now));
        _dispatcher.Events.ShouldBeEmpty();
        Nudge nudge = ReadNudge().ShouldNotBeNull();
        nudge.ShouldSatisfyAllConditions(
            () => nudge.Key.ShouldBe(new NudgeKey(_repository.Id, NudgePass.AwaitingInput, 1)),
            () => nudge.NotBefore.ShouldBe(Now + ActionableFeedbackPolicy.QuietPeriod));
    }

    [Fact]
    public async Task WhenABodyChangeOutlastsTheQuietPeriod_InputArrivesAsADescriptionEditAndTheWatchIsDeleted()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody);
        _provider.IssueBody = "The fixed requirements.";
        DateTimeOffset firstSeen = Now;
        await RunStepAtAsync(firstSeen);

        // Act
        await RunStepAtAsync(firstSeen.AddMinutes(2));

        // Assert
        AwaitingInputArrived arrived = _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem();
        arrived.ShouldSatisfyAllConditions(
            () => arrived.Input.ShouldBe(new ClarificationInput([new InputPart.DescriptionEdited()])),
            () => arrived.ArrivedAt.ShouldBe(firstSeen));
        LoadWatch().ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheBodyChangesAgain_TheQuietPeriodRestarts()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody);
        DateTimeOffset start = Now;
        _provider.IssueBody = "First edit.";
        await RunStepAtAsync(start);
        _provider.IssueBody = "Second edit.";
        await RunStepAtAsync(start.AddMinutes(1));

        // Act
        await RunStepAtAsync(start.AddMinutes(2));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingBodyChange
            .ShouldBe(new PendingBodyChange(IssueBodyHash.Of("Second edit."), start.AddMinutes(1)));
    }

    [Fact]
    public async Task WhenAnEditIsRevertedWithinTheQuietPeriod_NoInputArrives()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        string original = _provider.IssueBody;
        SeedWatch(issueNumber: 1, baselineBody: original);
        DateTimeOffset start = Now;
        _provider.IssueBody = "A mistaken edit.";
        await RunStepAtAsync(start);
        _provider.IssueBody = original;
        await RunStepAtAsync(start.AddMinutes(1));

        // Act
        await RunStepAtAsync(start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingBodyChange.ShouldBeNull();
    }

    [Fact]
    public async Task WhenACommentAndAnEditSettle_ExactlyOneRoundArrivesWithBothParts()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody);
        DateTimeOffset start = Now;
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(2002).WithBody("See the edited description.").WithCreatedAt(start.AddSeconds(-30)).Build(),
        ];
        _provider.IssueBody = "The fixed requirements.";
        await RunStepAtAsync(start);
        _dispatcher.Events.ShouldBeEmpty();

        // Act
        await RunStepAtAsync(start.AddMinutes(2));

        // Assert
        AwaitingInputArrived arrived = _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem();
        arrived.ShouldSatisfyAllConditions(
            () => arrived.Input.ShouldBe(new ClarificationInput(
                [new InputPart.Comments("See the edited description."), new InputPart.DescriptionEdited()])),
            () => arrived.ArrivedAt.ShouldBe(start));
    }

    [Fact]
    public async Task WhenTwoCommentsArriveAMinuteApart_NoInputArrivesUntilBothSettleThenBothArriveTogether()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        DateTimeOffset first = Now;
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(2002).WithBody("Use Postgres.").WithCreatedAt(first).Build(),
            new ProviderCommentBuilder().WithId(2003).WithBody("Actually, version 16.").WithCreatedAt(first.AddMinutes(1)).Build(),
        ];
        await RunStepAtAsync(first.AddMinutes(2.5));
        _dispatcher.Events.ShouldBeEmpty();
        ReadNudge().ShouldNotBeNull().NotBefore.ShouldBe(first.AddMinutes(3));

        // Act
        await RunStepAtAsync(first.AddMinutes(3));

        // Assert
        AwaitingInputArrived arrived = _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem();
        arrived.Input.ShouldBe(ClarificationInput.FromComments("Use Postgres.\n\nActually, version 16."));
        arrived.ArrivedAt.ShouldBe(first.AddMinutes(1));
    }

    [Fact]
    public async Task WhenTheQuestionIsOnAPullRequest_ABodyChangeIsNotWatched()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt));
        SeedWatch(issueNumber: 1, baselineBody: null);
        DateTimeOffset start = Now;
        _provider.IssueBody = "Ticked an acceptance criterion.";
        await RunStepAtAsync(start);

        // Act
        await RunStepAtAsync(start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        IssueWatch watch = LoadWatch().ShouldNotBeNull();
        watch.BaselineBodyHash.ShouldBeNull();
        watch.PendingBodyChange.ShouldBeNull();
    }

    [Fact]
    public async Task WhenPostedOnAPullRequest_StartsAWatchWithoutABodyBaseline()
    {
        // Arrange
        Seed(Builder().AwaitingInput());
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(555).ValueOrThrow());

        // Act
        await RunStepAsync();

        // Assert
        LoadWatch().ShouldNotBeNull().BaselineBodyHash.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAWaitPostedBeforeWatchesExisted_TheFirstReadBecomesTheBaselineAndCommentsStillArrive()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(2002).WithBody("Use Postgres.").WithCreatedAt(PostedAt.AddMinutes(5)).Build(),
        ];

        // Act
        await RunStepAsync();

        // Assert
        _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem().Input
            .ShouldBe(ClarificationInput.FromComments("Use Postgres."));
    }

    [Fact]
    public async Task WhenAWaitPostedBeforeWatchesExistedHasNoInput_StartsAWatchWithTheBodyAsBaseline()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));

        // Act
        await RunStepAsync();

        // Assert
        IssueWatch watch = LoadWatch().ShouldNotBeNull();
        watch.BaselineBodyHash.ShouldBe(IssueBodyHash.Of(_provider.IssueBody));
        watch.PendingBodyChange.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheBodyReadFailsWhileAChangeIsPending_NoInputArrives()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody);
        DateTimeOffset start = Now;
        _provider.IssueBody = "The fixed requirements.";
        await RunStepAtAsync(start);
        _provider.IssueStatusFails = true;

        // Act
        await RunStepAtAsync(start.AddMinutes(3));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingBodyChange.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenAFullPassRuns_DeletesTheWatchesOfIssuesNoLongerAwaitingInput()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody);
        SeedWatch(issueNumber: 99, baselineBody: "Left over.");

        // Act
        await RunStepAsync();

        // Assert
        LoadWatch().ShouldNotBeNull().IssueNumber.ShouldBe(1);
    }

    [Fact]
    public async Task WhenANudgedPassRuns_KeepsTheWatchesOfOtherIssues()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody);
        SeedWatch(issueNumber: 99, baselineBody: "Another issue.");

        // Act
        await BuildSut().RunAsync(_repository, _provider, issueNumber: 1, Now, TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        _dbContext.Set<IssueWatch>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task WhenNoIssueIsAwaitingInput_AFullPassStillDeletesLeftoverWatches()
    {
        // Arrange
        SeedWatch(issueNumber: 99, baselineBody: "Left over.");

        // Act
        await RunStepAsync();

        // Assert
        LoadWatch().ShouldBeNull();
    }

    [Fact]
    public async Task WhenAFullPassRuns_KeepsTheWatchOfAFailedTriage()
    {
        // Arrange
        SeedFailedTriageWatch(issueNumber: 99, baselineBody: "Failed triage.");

        // Act
        await RunStepAsync();

        // Assert
        LoadWatch().ShouldNotBeNull().Wait.ShouldBe(WatchedWait.TriageFailed);
    }

    [Fact]
    public async Task WhenAPostedWaitFindsAFailedTriagesWatch_ReplacesItWithItsOwnWithoutCountingTheBodyAsInput()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedFailedTriageWatch(issueNumber: 1, baselineBody: "An older body.");

        // Act
        await RunStepAsync();

        // Assert
        IssueWatch watch = LoadWatch().ShouldNotBeNull();
        watch.ShouldSatisfyAllConditions(
            () => watch.Wait.ShouldBe(WatchedWait.AwaitingInput),
            () => watch.BaselineBodyHash.ShouldBe(IssueBodyHash.Of(_provider.IssueBody)),
            () => watch.PendingBodyChange.ShouldBeNull());
    }

    private static readonly DateTimeOffset ReminderDueAt = PostedAt.AddDays(3);

    [Fact]
    public async Task WhenPostedForAContinuation_StartsAWatchWithTheBranchHeadReadInThatPass()
    {
        // Arrange
        Seed(Builder().AwaitingInputWithoutPullRequest(branchHasCommits: true));
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(556).ValueOrThrow());

        // Act
        await RunStepAsync();

        // Assert
        IssueWatch watch = LoadWatch().ShouldNotBeNull();
        watch.ShouldSatisfyAllConditions(
            () => watch.BaselineHead.ShouldBe(new BranchHead.Present(PostedHead)),
            () => watch.PendingHeadChange.ShouldBeNull(),
            () => _provider.BranchHeadReads.ShouldBe([BranchName]));
    }

    [Fact]
    public async Task WhenPostedForARevision_StartsAWatchWithThePullRequestHeadWithoutABranchRead()
    {
        // Arrange
        Seed(Builder().AwaitingInput());
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(555).ValueOrThrow());

        // Act
        await RunStepAsync();

        // Assert
        LoadWatch().ShouldNotBeNull().BaselineHead.ShouldBe(new BranchHead.Present(PostedHead));
        _provider.BranchHeadReads.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPostedForAFreshRun_WatchesNoHead()
    {
        // Arrange
        Seed(Builder().AwaitingInputWithoutPullRequest(branchHasCommits: false));
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(556).ValueOrThrow());

        // Act
        await RunStepAsync();

        // Assert
        LoadWatch().ShouldNotBeNull().BaselineHead.ShouldBeNull();
        _provider.BranchHeadReads.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAContinuationsBranchIsPushed_RecordsItAsPendingAndNudgesWhenItSettles()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody, new BranchHead.Present(PostedHead));
        _provider.BranchHead = new BranchHead.Present(PushedHead);

        // Act
        await RunStepAtAsync(Now);

        // Assert
        LoadWatch().ShouldNotBeNull().PendingHeadChange.ShouldBe(new PendingHeadChange(PushedHead, Now));
        _dispatcher.Events.ShouldBeEmpty();
        ReadNudge().ShouldNotBeNull().NotBefore.ShouldBe(Now + ActionableFeedbackPolicy.QuietPeriod);
    }

    [Fact]
    public async Task WhenAContinuationsPushOutlastsTheQuietPeriod_InputArrivesAsABranchUpdateAndTheWatchIsDeleted()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody, new BranchHead.Present(PostedHead));
        _provider.BranchHead = new BranchHead.Present(PushedHead);
        DateTimeOffset firstSeen = Now;
        await RunStepAtAsync(firstSeen);

        // Act
        await RunStepAtAsync(firstSeen.AddMinutes(2));

        // Assert
        AwaitingInputArrived arrived = _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem();
        arrived.ShouldSatisfyAllConditions(
            () => arrived.Input.ShouldBe(new ClarificationInput([new InputPart.BranchUpdated(PostedHead, PushedHead)])),
            () => arrived.ArrivedAt.ShouldBe(firstSeen));
        LoadWatch().ShouldBeNull();
    }

    [Fact]
    public async Task WhenARevisionsPullRequestHeadMoves_InputArrivesAsABranchUpdateOnceItSettles()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt));
        SeedWatch(issueNumber: 1, baselineBody: null, new BranchHead.Present(PostedHead));
        _provider.PullRequestStatus = new PullRequestStatus(IsClosed: false, IsMerged: false, HeadSha: PushedHead, IsDraft: false);
        DateTimeOffset firstSeen = Now;
        await RunStepAtAsync(firstSeen);
        _dispatcher.Events.ShouldBeEmpty();

        // Act
        await RunStepAtAsync(firstSeen.AddMinutes(2));

        // Assert
        _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem().Input
            .ShouldBe(new ClarificationInput([new InputPart.BranchUpdated(PostedHead, PushedHead)]));
        _provider.BranchHeadReads.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenARevisionsPullRequestStatusHasNoHead_NothingIsObserved()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt));
        SeedWatch(issueNumber: 1, baselineBody: null, baselineHead: null);
        _provider.PullRequestStatus = new PullRequestStatus(IsClosed: false, IsMerged: false, HeadSha: null, IsDraft: false);

        // Act
        await RunStepAtAsync(Now);

        // Assert
        LoadWatch().ShouldNotBeNull().BaselineHead.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAPushAndACommentSettle_ExactlyOneRoundArrivesWithBothParts()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody, new BranchHead.Present(PostedHead));
        DateTimeOffset start = Now;
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(2002).WithBody("I pushed the workflow change.").WithCreatedAt(start.AddSeconds(-30)).Build(),
        ];
        _provider.BranchHead = new BranchHead.Present(PushedHead);
        await RunStepAtAsync(start);
        _dispatcher.Events.ShouldBeEmpty();

        // Act
        await RunStepAtAsync(start.AddMinutes(2));

        // Assert
        AwaitingInputArrived arrived = _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem();
        arrived.ShouldSatisfyAllConditions(
            () => arrived.Input.ShouldBe(new ClarificationInput(
                [new InputPart.Comments("I pushed the workflow change."), new InputPart.BranchUpdated(PostedHead, PushedHead)])),
            () => arrived.ArrivedAt.ShouldBe(start));
    }

    [Fact]
    public async Task WhenAPushIsForcedBackToTheBaselineWithinTheQuietPeriod_NoInputArrives()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody, new BranchHead.Present(PostedHead));
        DateTimeOffset start = Now;
        _provider.BranchHead = new BranchHead.Present(PushedHead);
        await RunStepAtAsync(start);
        _provider.BranchHead = new BranchHead.Present(PostedHead);

        // Act
        await RunStepAtAsync(start.AddMinutes(1));
        await RunStepAtAsync(start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingHeadChange.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheBranchIsDeletedWhileWaiting_NoInputArrives()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody, new BranchHead.Present(PostedHead));
        _provider.BranchHead = new BranchHead.Absent();
        DateTimeOffset start = Now;
        await RunStepAtAsync(start);

        // Act
        await RunStepAtAsync(start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingHeadChange.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheHeadReadFailedAtPost_PostsWithoutABaselineAndTheNextReadRecordsItWithoutInput()
    {
        // Arrange
        Seed(Builder().AwaitingInputWithoutPullRequest(branchHasCommits: true));
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(556).ValueOrThrow());
        _provider.BranchHead = null;
        await RunStepAtAsync(Now);
        LoadWatch().ShouldNotBeNull().BaselineHead.ShouldBeNull();
        _provider.BranchHead = new BranchHead.Present(PushedHead);

        // Act
        await RunStepAtAsync(Now.AddMinutes(5));

        // Assert
        IssueWatch watch = LoadWatch().ShouldNotBeNull();
        watch.BaselineHead.ShouldBe(new BranchHead.Present(PushedHead));
        watch.PendingHeadChange.ShouldBeNull();
        _dispatcher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheHeadReadFailsWhileAPushIsPending_NoInputArrives()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        SeedWatch(issueNumber: 1, baselineBody: _provider.IssueBody, new BranchHead.Present(PostedHead));
        DateTimeOffset start = Now;
        _provider.BranchHead = new BranchHead.Present(PushedHead);
        await RunStepAtAsync(start);
        _provider.BranchHead = null;

        // Act
        await RunStepAtAsync(start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingHeadChange.ShouldBe(new PendingHeadChange(PushedHead, start));
    }

    [Fact]
    public async Task WhenTheReminderDelayHasPassedWithNoInput_PostsOneReminderInTheQuestionsThreadAndRegistersIt()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt));
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(777).ValueOrThrow());

        // Act
        await RunStepAtAsync(ReminderDueAt.AddMinutes(1));

        // Assert
        QuestionReminder.Sent sent = LoadAwaiting().Reminder.ShouldBeOfType<QuestionReminder.Sent>();
        sent.ShouldSatisfyAllConditions(
            () => sent.CommentId.ShouldBe(777),
            () => sent.SentAt.ShouldBe(ReminderDueAt.AddMinutes(1)),
            () => _provider.PostedThreads.ShouldHaveSingleItem().ShouldBe(new CommentThread.PullRequest(PrUrl)),
            () => _provider.PostedBodies.ShouldHaveSingleItem().ShouldContain("still waiting for input"),
            () => _dbContext.Set<PostedComment>().ShouldHaveSingleItem().CommentId.Value.ShouldBe(777),
            () => _dispatcher.Events.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenOnTheIssueThread_TheReminderGoesOnTheIssue()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(777).ValueOrThrow());

        // Act
        await RunStepAtAsync(ReminderDueAt);

        // Assert
        _provider.PostedThreads.ShouldHaveSingleItem().ShouldBe(new CommentThread.Issue(1));
    }

    [Fact]
    public async Task WhenTheReminderWasSent_NoFurtherReminderIsPosted()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt));
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(777).ValueOrThrow());
        await RunStepAtAsync(ReminderDueAt);

        // Act
        await RunStepAtAsync(ReminderDueAt.AddDays(7));

        // Assert
        _provider.PostedBodies.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenTheReminderDelayHasNotPassed_PostsNothing()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt));

        // Act
        await RunStepAtAsync(ReminderDueAt.AddMinutes(-1));

        // Assert
        _provider.PostedThreads.ShouldBeEmpty();
        LoadAwaiting().Reminder.ShouldBeOfType<QuestionReminder.Unsent>();
    }

    [Fact]
    public async Task WhenInputArrivesOnceTheReminderIsDue_TheIssueResumesAndNoReminderIsPosted()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(2002).WithAuthorLogin("operator").WithBody("Use Postgres.").WithCreatedAt(PostedAt.AddMinutes(5)).Build(),
        ];

        // Act
        await RunStepAtAsync(ReminderDueAt);

        // Assert
        _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem();
        _provider.PostedThreads.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAReplyIsStillInsideTheQuietPeriodOnceTheReminderIsDue_NoReminderIsPosted()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(2002).WithBody("Use Postgres.").WithCreatedAt(ReminderDueAt.AddSeconds(-30)).Build(),
        ];

        // Act
        await RunStepAtAsync(ReminderDueAt);

        // Assert
        _provider.PostedThreads.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheReminderPostFailsTransiently_ItRetriesWithTheQuestionPostsBackoffAndRegistersNothing()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt));
        _provider.PostResult = Result<ProviderCommentId>.Fail(CommentPostErrors.Transient("503"));

        // Act
        await RunStepAtAsync(ReminderDueAt);

        // Assert
        QuestionReminder.Failed failed = LoadAwaiting().Reminder.ShouldBeOfType<QuestionReminder.Failed>();
        failed.ShouldSatisfyAllConditions(
            () => failed.NextAttemptAt.ShouldBe(ReminderDueAt + QuestionPost.FirstRetryDelay),
            () => _dbContext.Set<PostedComment>().ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenAReminderRetryIsNotDue_ReplyDetectionStillRuns()
    {
        // Arrange
        AwaitingInputIssue awaiting = Builder().AwaitingInputPosted(PostedAt, withPullRequest: false);
        awaiting.RecordReminderPostFailed("503", transient: true, ReminderDueAt);
        Seed(awaiting);
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(2002).WithAuthorLogin("operator").WithBody("Use Postgres.").WithCreatedAt(ReminderDueAt.AddSeconds(10)).Build(),
        ];

        // Act
        await RunStepAtAsync(ReminderDueAt.AddMinutes(5));

        // Assert
        _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem();
        _provider.PostedThreads.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAReplyFollowsTheReminder_InputArrivesWithoutTheReminderComment()
    {
        // Arrange
        Seed(Builder().AwaitingInputPosted(PostedAt, withPullRequest: false));
        _provider.PostResult = Result<ProviderCommentId>.Ok(ProviderCommentId.Create(777).ValueOrThrow());
        await RunStepAtAsync(ReminderDueAt);
        _provider.IssueComments =
        [
            new ProviderCommentBuilder().WithId(777).WithAuthorLogin("operator").WithBody("Reminder").WithCreatedAt(ReminderDueAt).Build(),
            new ProviderCommentBuilder().WithId(2002).WithAuthorLogin("operator").WithBody("Use Postgres.").WithCreatedAt(ReminderDueAt.AddMinutes(1)).Build(),
        ];

        // Act
        await RunStepAtAsync(ReminderDueAt.AddMinutes(10));

        // Assert
        _dispatcher.Events.OfType<AwaitingInputArrived>().ShouldHaveSingleItem().Input
            .ShouldBe(ClarificationInput.FromComments("Use Postgres."));
    }

    private sealed class ScriptedProvider : IIssueProvider
    {
        public bool IssueClosed { get; set; }

        public string IssueBody { get; set; } = "The original description.";

        public bool IssueStatusFails { get; set; }

        public int IssueStatusReads { get; private set; }

        public PullRequestStatus PullRequestStatus { get; set; } = new(IsClosed: false, IsMerged: false, HeadSha: PostedHead, IsDraft: false);

        // The branch head a continuation's branch read returns; null makes the read fail.
        public BranchHead? BranchHead { get; set; } = new BranchHead.Present(PostedHead);

        public List<string> BranchHeadReads { get; } = [];

        public Result<ProviderCommentId> PostResult { get; set; } =
            Result<ProviderCommentId>.Fail(CommentPostErrors.Permanent("not scripted"));

        public List<CommentThread> PostedThreads { get; } = [];

        public List<string> PostedBodies { get; } = [];

        public IReadOnlyList<ProviderComment> IssueComments { get; set; } = [];

        public DateTimeOffset? IssueFeedbackSince { get; private set; }

        public ReviewFeedback ReviewFeedback { get; set; } = new([], 0, null);

        public DateTimeOffset? ReviewFeedbackSince { get; private set; }

        // The step reads the closed state together with the body; a call here would be an extra request.
        public Task<Result<bool>> IsIssueClosedAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<IssueStatus>> GetIssueStatusAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken)
        {
            IssueStatusReads++;
            return Task.FromResult(IssueStatusFails
                ? Result<IssueStatus>.Fail(new Error("Test.Unavailable", "503"))
                : Result<IssueStatus>.Ok(new IssueStatus(IssueClosed, IssueBody)));
        }

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<PullRequestStatus>.Ok(PullRequestStatus));

        public Task<Result<BranchHead>> GetBranchHeadAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
        {
            BranchHeadReads.Add(branchName);
            return Task.FromResult(BranchHead is { } head
                ? Result<BranchHead>.Ok(head)
                : Result<BranchHead>.Fail(new Error("Test.Unavailable", "503")));
        }

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

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(RepositorySlug slug, string branchName, CommitSha? publishedHead, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<Checks>> GetChecksAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => Now;
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
