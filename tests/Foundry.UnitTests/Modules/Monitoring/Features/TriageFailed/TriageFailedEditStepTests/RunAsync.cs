using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.TriageFailed;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.TriageFailed.TriageFailedEditStepTests;

/// <summary>
/// Tests for the failed-triage pass (issue #773): it keeps an <see cref="IssueWatch"/> on each <c>triage_failed</c>
/// issue's body and sends the issue back to triage once an edit settles.
/// </summary>
public sealed class RunAsync : IAsyncDisposable
{
    private const string OriginalBody = "The original description.";
    private const string EditedBody = "The fixed requirements.";

    private static readonly DateTimeOffset Start = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _dispatcher = new();
    private readonly WebhookNudgeQueue _nudgeQueue = new(NullLogger<WebhookNudgeQueue>.Instance);
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

    private TriageFailedEditStep BuildSut() =>
        new(
            new TriageFailedIssueQueries(_dbContext),
            _dbContext,
            _dispatcher,
            _nudgeQueue,
            NullLogger<TriageFailedEditStep>.Instance);

    private void SeedTriageFailed(int issueNumber = 1)
    {
        _dbContext.Set<Issue>().Add(new IssueBuilder()
            .WithMonitoredRepositoryId(_repository.Id)
            .WithIssueNumber(issueNumber)
            .WithFailedAt(Start.AddMinutes(-1))
            .TriageFailed());
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private void SeedAwaitingInputWatch(int issueNumber)
    {
        _dbContext.Set<IssueWatch>().Add(IssueWatch.Start(_repository.Id, issueNumber, IssueBodyHash.Of("Waiting."), baselineHead: null));
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private void SeedFailedTriageWatch(int issueNumber)
    {
        _dbContext.Set<IssueWatch>().Add(IssueWatch.StartForFailedTriage(_repository.Id, issueNumber, IssueBodyHash.Of("Gone.")));
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
    }

    private async Task RunStepAtAsync(DateTimeOffset at, int? issueNumber = null)
    {
        await BuildSut().RunAsync(_repository, _provider, issueNumber, at, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private IssueWatch? LoadWatch(int issueNumber = 1)
    {
        _dbContext.ChangeTracker.Clear();
        return _dbContext.Set<IssueWatch>().SingleOrDefault(watch => watch.IssueNumber == issueNumber);
    }

    [Fact]
    public async Task WhenAPassFirstSeesAFailedTriage_StartsAWatchWithTheBodyAsBaseline()
    {
        // Arrange
        SeedTriageFailed();

        // Act
        await RunStepAtAsync(Start);

        // Assert
        IssueWatch watch = LoadWatch().ShouldNotBeNull();
        watch.ShouldSatisfyAllConditions(
            () => watch.Wait.ShouldBe(WatchedWait.TriageFailed),
            () => watch.BaselineBodyHash.ShouldBe(IssueBodyHash.Of(OriginalBody)),
            () => watch.PendingBodyChange.ShouldBeNull(),
            () => _dispatcher.Events.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenTheBodyIsUnchanged_NothingHappens()
    {
        // Arrange
        SeedTriageFailed();
        await RunStepAtAsync(Start);

        // Act
        await RunStepAtAsync(Start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingBodyChange.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheBodyChanges_RecordsItAsPendingWithoutSendingTheIssueBack()
    {
        // Arrange
        SeedTriageFailed();
        await RunStepAtAsync(Start);
        _provider.IssueBody = EditedBody;

        // Act
        await RunStepAtAsync(Start.AddMinutes(1));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingBodyChange
            .ShouldBe(new PendingBodyChange(IssueBodyHash.Of(EditedBody), Start.AddMinutes(1)));
    }

    [Fact]
    public async Task WhenABodyChangeOutlastsTheQuietPeriod_RaisesTheEditAndDeletesTheWatchInTheSameSave()
    {
        // Arrange
        SeedTriageFailed();
        await RunStepAtAsync(Start);
        _provider.IssueBody = EditedBody;
        await RunStepAtAsync(Start.AddMinutes(1));

        // Act
        await RunStepAtAsync(Start.AddMinutes(3));

        // Assert
        _dispatcher.Events.ShouldHaveSingleItem()
            .ShouldBe(new TriageFailedIssueEdited(_repository.Id, 1, Start.AddMinutes(1)));
        LoadWatch().ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheBodyChangesAgain_TheQuietPeriodRestarts()
    {
        // Arrange
        SeedTriageFailed();
        await RunStepAtAsync(Start);
        _provider.IssueBody = EditedBody;
        await RunStepAtAsync(Start.AddMinutes(1));
        _provider.IssueBody = "Edited once more.";
        await RunStepAtAsync(Start.AddMinutes(2));

        // Act
        await RunStepAtAsync(Start.AddMinutes(3.5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingBodyChange.ShouldNotBeNull().FirstSeenAt.ShouldBe(Start.AddMinutes(2));
    }

    [Fact]
    public async Task WhenAnEditIsRevertedWithinTheQuietPeriod_NothingHappens()
    {
        // Arrange
        SeedTriageFailed();
        await RunStepAtAsync(Start);
        _provider.IssueBody = EditedBody;
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
    public async Task WhenTheBodyReadFailsOnTheFirstPass_StartsNoWatchAndTheNextReadBecomesTheBaseline()
    {
        // Arrange
        SeedTriageFailed();
        _provider.IssueStatusFails = true;
        await RunStepAtAsync(Start);
        LoadWatch().ShouldBeNull();
        _provider.IssueStatusFails = false;

        // Act
        await RunStepAtAsync(Start.AddMinutes(1));

        // Assert
        LoadWatch().ShouldNotBeNull().BaselineBodyHash.ShouldBe(IssueBodyHash.Of(OriginalBody));
    }

    [Fact]
    public async Task WhenTheBodyReadFailsWhileAChangeIsPending_LeavesTheWatchAndRaisesNothing()
    {
        // Arrange
        SeedTriageFailed();
        await RunStepAtAsync(Start);
        _provider.IssueBody = EditedBody;
        await RunStepAtAsync(Start.AddMinutes(1));
        _provider.IssueStatusFails = true;

        // Act
        await RunStepAtAsync(Start.AddMinutes(5));

        // Assert
        _dispatcher.Events.ShouldBeEmpty();
        LoadWatch().ShouldNotBeNull().PendingBodyChange
            .ShouldBe(new PendingBodyChange(IssueBodyHash.Of(EditedBody), Start.AddMinutes(1)));
    }

    [Fact]
    public async Task WhenTheIssueIsClosed_ComparesNothing()
    {
        // Arrange
        SeedTriageFailed();
        await RunStepAtAsync(Start);
        _provider.IssueBody = EditedBody;
        _provider.IssueClosed = true;

        // Act
        await RunStepAtAsync(Start.AddMinutes(1));

        // Assert
        LoadWatch().ShouldNotBeNull().PendingBodyChange.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAnIssueLeftTriageFailed_DeletesItsWatch()
    {
        // Arrange
        SeedFailedTriageWatch(issueNumber: 7);

        // Act
        await RunStepAtAsync(Start);

        // Assert
        LoadWatch(7).ShouldBeNull();
    }

    [Fact]
    public async Task WhenAnIssueAwaitsInput_KeepsItsWatch()
    {
        // Arrange
        SeedAwaitingInputWatch(issueNumber: 7);

        // Act
        await RunStepAtAsync(Start);

        // Assert
        LoadWatch(7).ShouldNotBeNull().Wait.ShouldBe(WatchedWait.AwaitingInput);
    }

    [Fact]
    public async Task WhenAFailedTriageHasAWatchLeftFromAnAwaitingInputWait_ReplacesItWithTheBodyAsBaseline()
    {
        // Arrange
        SeedTriageFailed();
        SeedAwaitingInputWatch(issueNumber: 1);

        // Act
        await RunStepAtAsync(Start);

        // Assert
        IssueWatch watch = LoadWatch().ShouldNotBeNull();
        watch.ShouldSatisfyAllConditions(
            () => watch.Wait.ShouldBe(WatchedWait.TriageFailed),
            () => watch.BaselineBodyHash.ShouldBe(IssueBodyHash.Of(OriginalBody)),
            () => _dispatcher.Events.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenABodyChangeIsInsideTheQuietPeriod_EnqueuesANudgeForTheIssueWhenItSettles()
    {
        // Arrange
        SeedTriageFailed();
        await RunStepAtAsync(Start);
        _provider.IssueBody = EditedBody;

        // Act
        await RunStepAtAsync(Start.AddMinutes(1));

        // Assert
        _nudgeQueue.Reader.TryRead(out Nudge nudge).ShouldBeTrue();
        nudge.ShouldBe(new Nudge(
            new NudgeKey(_repository.Id, NudgePass.TriageFailedEdit, 1),
            Start.AddMinutes(1) + ActionableFeedbackPolicy.QuietPeriod));
    }

    [Fact]
    public async Task WhenTheBodyIsUnchanged_EnqueuesNoNudge()
    {
        // Arrange
        SeedTriageFailed();

        // Act
        await RunStepAtAsync(Start);

        // Assert
        _nudgeQueue.Reader.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenNudgedForOneFailedTriage_ReadsOnlyThatIssue()
    {
        // Arrange
        SeedTriageFailed(issueNumber: 1);
        SeedTriageFailed(issueNumber: 2);

        // Act
        await RunStepAtAsync(Start, issueNumber: 2);

        // Assert
        _provider.IssueStatusReads.ShouldBe(1);
        LoadWatch(1).ShouldBeNull();
        LoadWatch(2).ShouldNotBeNull().Wait.ShouldBe(WatchedWait.TriageFailed);
    }

    [Fact]
    public async Task WhenNudgedForAnIssueThatIsNotTriageFailed_ReadsNoIssue()
    {
        // Arrange
        SeedTriageFailed(issueNumber: 1);

        // Act
        await RunStepAtAsync(Start, issueNumber: 2);

        // Assert
        _provider.IssueStatusReads.ShouldBe(0);
    }

    [Fact]
    public async Task WhenNudged_KeepsTheWatchesOfOtherIssuesThatLeftTriageFailed()
    {
        // Arrange
        SeedTriageFailed(issueNumber: 1);
        SeedFailedTriageWatch(issueNumber: 7);

        // Act
        await RunStepAtAsync(Start, issueNumber: 1);

        // Assert
        LoadWatch(7).ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenNoIssueIsTriageFailed_ReadsNoIssue()
    {
        // Act
        await RunStepAtAsync(Start);

        // Assert
        _provider.IssueStatusReads.ShouldBe(0);
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
