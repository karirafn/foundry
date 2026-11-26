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

public sealed class GetIssueDetailAsync_Demand : IAsyncDisposable
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetIssueDetailAsync_Demand()
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

    private async Task<IssueDetail> SaveAndGetDetailAsync(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(issue.Id, TestContext.Current.CancellationToken);
        return result.ShouldBeOfType<Result<IssueDetail>.Success>().Value;
    }

    private static Issue ReadyFromTriage(TriageVerdict.Ready verdict) =>
        new IssueBuilder().WithMonitoredRepositoryId(RepositoryId).TriageInProgress().MarkReady(verdict, [], Now);

    [Fact]
    public async Task WhenTriageWasSkipped_TheDetailCarriesNoDemandAndNoReason()
    {
        // Arrange
        Issue issue = new IssueBuilder().WithMonitoredRepositoryId(RepositoryId).FreshQueued();

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(issue);

        // Assert
        detail.ShouldSatisfyAllConditions(
            () => detail.ImplementDemand.ShouldBeNull(),
            () => detail.DemandReason.ShouldBeNull(),
            () => detail.DemandEscalated.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenTriageJudgedTheIssueRoutine_TheDetailCarriesRoutineAndTheReason()
    {
        // Arrange
        Issue issue = ReadyFromTriage(new TriageVerdict.Ready(IssueKind.Documentation, Now, ImplementDemand.Routine, "none"));

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(issue);

        // Assert
        detail.ShouldSatisfyAllConditions(
            () => detail.ImplementDemand.ShouldBe(ImplementDemand.Routine),
            () => detail.DemandReason.ShouldBe("none"),
            () => detail.DemandEscalated.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenTriageJudgedTheIssueDemanding_TheDetailCarriesDemandingAndTheReason()
    {
        // Arrange
        Issue issue = ReadyFromTriage(
            new TriageVerdict.Ready(IssueKind.Feature, Now, ImplementDemand.Demanding, "Adds a persistence migration."));

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(issue);

        // Assert
        detail.ShouldSatisfyAllConditions(
            () => detail.ImplementDemand.ShouldBe(ImplementDemand.Demanding),
            () => detail.DemandReason.ShouldBe("Adds a persistence migration."),
            () => detail.DemandEscalated.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenTheVerdictPredatesTheDemand_TheDetailCarriesDemandingWithNoReason()
    {
        // Arrange
        Issue issue = ReadyFromTriage(new TriageVerdict.Ready(IssueKind.Feature, Now));

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(issue);

        // Assert
        detail.ShouldSatisfyAllConditions(
            () => detail.ImplementDemand.ShouldBe(ImplementDemand.Demanding),
            () => detail.DemandReason.ShouldBeNull());
    }

    [Fact]
    public async Task WhenARoutineVerdictFollowsAnEscalation_TheDetailCarriesDemandingEscalatedAndTheVerdictsReason()
    {
        // Arrange
        Issue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .RevisionQueuedFromReviewFindings()
            .Claim(WorkerRunId.New(), Now)
            .MarkRebasePending(Now, Now)
            .MarkChecksPending(new RebaseOutcome.UpToDate(), Now)
            .ToReview(new ChecksOutcome.Passed(), Head, Now)
            .RetriageRewrittenSpec(Now)
            .Claim(WorkerRunId.New(), Now)
            .MarkReady(new TriageVerdict.Ready(IssueKind.Feature, Now, ImplementDemand.Routine, "Copies the sibling slice."), [], Now);

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(issue);

        // Assert
        detail.ShouldSatisfyAllConditions(
            () => detail.ImplementDemand.ShouldBe(ImplementDemand.Demanding),
            () => detail.DemandReason.ShouldBe("Copies the sibling slice."),
            () => detail.DemandEscalated.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenAnIssueEscalatedWithoutAReadyVerdict_TheDetailCarriesTheEscalationAlone()
    {
        // Arrange
        Issue issue = new IssueBuilder().WithMonitoredRepositoryId(RepositoryId).RevisionQueuedFromReviewFindings();

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(issue);

        // Assert
        detail.ShouldSatisfyAllConditions(
            () => detail.ImplementDemand.ShouldBeNull(),
            () => detail.DemandReason.ShouldBeNull(),
            () => detail.DemandEscalated.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenTriageAskedQuestions_TheDetailCarriesNoDemand()
    {
        // Arrange
        Issue issue = new IssueBuilder().WithMonitoredRepositoryId(RepositoryId).AwaitingInputFromTriage(IssueKind.Feature);

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(issue);

        // Assert
        detail.ShouldSatisfyAllConditions(
            () => detail.ImplementDemand.ShouldBeNull(),
            () => detail.DemandReason.ShouldBeNull());
    }
}
