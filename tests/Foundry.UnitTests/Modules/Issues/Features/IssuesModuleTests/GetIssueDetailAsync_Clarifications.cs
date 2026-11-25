using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetIssueDetailAsync_Clarifications : IAsyncDisposable
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetIssueDetailAsync_Clarifications()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        StubRepositorySlugQueries slugQueries = new();
        slugQueries.AddSlug(RepositoryId, "owner/repo", "github");
        _sut = new IssueQueries(
            _dbContext,
            slugQueries,
            new NullRepositoryEligibilityQuery(),
            new NullWorkerRunQueries(),
            new InFlightWorkerCountQuery(_dbContext, new NullWorkerRunQueries()),
            new NullGlobalSettingsQueries(),
            new NullMergeQueueQueries(),
            new NullRepositoryLifecycleSettingsQueries());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static IssueBuilder Builder() =>
        new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithUrl(ProviderUrl.Create("https://github.com/owner/repo/issues/42").ValueOrThrow());

    private async Task<IssueDetail> SaveAndGetDetailAsync(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(issue.Id, TestContext.Current.CancellationToken);
        return result.ShouldBeOfType<Result<IssueDetail>.Success>().Value;
    }

    [Fact]
    public async Task WhenTriageDecidedWithoutAsking_ReturnsADecidedRoundWithItsDecisions()
    {
        // Arrange
        TriageDecision decision = TriageDecisionBuilder.Build(
            question: "Which retry cap?",
            decision: "Three attempts.",
            ground: TriageDecisionGround.SettledBusiness,
            evidence: ["src/Retry.cs:8 caps at three"]);
        Issue queued = Builder().TriageInProgress()
            .MarkReady(new TriageVerdict.Ready(IssueKind.Bug, DecidedAt), [decision], DecidedAt);

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(queued);

        // Assert
        ClarificationDetails round = detail.Clarifications.ShouldHaveSingleItem();
        round.ShouldSatisfyAllConditions(
            () => round.Questions.ShouldBeEmpty(),
            () => round.Input.ShouldBeNull(),
            () => round.AskedAt.ShouldBe(DecidedAt),
            () => round.AnsweredAt.ShouldBeNull(),
            () => round.Decisions.ShouldBe(
            [
                new ClarificationDecisionDetails(
                    "Which retry cap?",
                    "Three attempts.",
                    ClarificationDecisionDetails.SettledBusinessGround,
                    ["src/Retry.cs:8 caps at three"]),
            ],
            ignoreOrder: false));
    }

    [Fact]
    public async Task WhenTriageAskedAndDecided_ReturnsTheQuestionsBesideTheImplementationDetailDecision()
    {
        // Arrange
        TriageDecision decision = TriageDecisionBuilder.Build(ground: TriageDecisionGround.ImplementationDetail);
        Issue awaiting = Builder().TriageInProgress().MarkNeedsInput(
            new TriageVerdict.NeedsInput(IssueKind.Bug, IssueBuilder.DefaultQuestions, DecidedAt),
            [decision],
            DecidedAt);

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(awaiting);

        // Assert
        ClarificationDetails round = detail.Clarifications.ShouldHaveSingleItem();
        round.ShouldSatisfyAllConditions(
            () => round.Questions.ShouldNotBeEmpty(),
            () => round.Decisions.ShouldHaveSingleItem().Ground.ShouldBe(ClarificationDecisionDetails.ImplementationDetailGround));
    }

    [Fact]
    public async Task WhenARoundHasNoDecisions_ReturnsAnEmptyDecisionList()
    {
        // Arrange
        Issue awaiting = Builder().AwaitingInputFromTriage(IssueKind.Bug);

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(awaiting);

        // Assert
        detail.Clarifications.ShouldHaveSingleItem().Decisions.ShouldBeEmpty();
    }

    // The detail exposes no field for a requested round yet, so the round is left out rather than shown as a question round.
    [Fact]
    public async Task WhenAPersonsReviewFeedbackWasRecorded_LeavesTheRequestedRoundOut()
    {
        // Arrange
        Issue revision = Builder().RevisionQueued();

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(revision);

        // Assert
        detail.Clarifications.ShouldBeEmpty();
    }
}
