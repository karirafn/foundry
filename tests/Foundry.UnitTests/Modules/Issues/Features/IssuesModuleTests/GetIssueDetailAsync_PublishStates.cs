using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetIssueDetailAsync_PublishStates : IAsyncDisposable
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetIssueDetailAsync_PublishStates()
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
            new InFlightWorkerCountQuery(_dbContext),
            new NullGlobalSettingsQueries());
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

    [Fact]
    public async Task WhenBranchDivergedOnGitHub_ReturnsBothBranchesWithTreeUrls()
    {
        // Arrange
        Issue diverged = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithUrl(ProviderUrl.Create("https://github.com/owner/repo/issues/42").ValueOrThrow())
            .WithBranchName("feat/42-add-retry")
            .BranchDiverged("feat/42-add-retry-run-abc");

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(diverged);

        // Assert
        IssueStateDetails stateDetails = detail.StateDetails.ShouldNotBeNull();
        stateDetails.ShouldSatisfyAllConditions(
            () => detail.State.ShouldBe("branch_diverged"),
            () => stateDetails.BranchName.ShouldBe("feat/42-add-retry"),
            () => stateDetails.BranchUrl.ShouldBe("https://github.com/owner/repo/tree/feat/42-add-retry"),
            () => stateDetails.SideBranchName.ShouldBe("feat/42-add-retry-run-abc"),
            () => stateDetails.SideBranchUrl.ShouldBe("https://github.com/owner/repo/tree/feat/42-add-retry-run-abc"));
    }

    [Fact]
    public async Task WhenBranchDivergedOnGitLab_ReturnsGitLabTreeUrls()
    {
        // Arrange
        Issue diverged = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithUrl(ProviderUrl.Create("https://gitlab.example.com/group/sub/proj/-/issues/42").ValueOrThrow())
            .WithBranchName("feat/42-add-retry")
            .BranchDiverged("feat/42-add-retry-run-abc");

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(diverged);

        // Assert
        IssueStateDetails stateDetails = detail.StateDetails.ShouldNotBeNull();
        stateDetails.ShouldSatisfyAllConditions(
            () => stateDetails.BranchUrl.ShouldBe("https://gitlab.example.com/group/sub/proj/-/tree/feat/42-add-retry"),
            () => stateDetails.SideBranchUrl.ShouldBe("https://gitlab.example.com/group/sub/proj/-/tree/feat/42-add-retry-run-abc"));
    }

    [Fact]
    public async Task WhenPublishFailed_ReturnsRunBranchAndReasonWithoutBranchUrls()
    {
        // Arrange
        Issue publishFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithBranchName("feat/1-test")
            .WithFailureReason("remote unreachable")
            .PublishFailed();

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(publishFailed);

        // Assert
        IssueStateDetails stateDetails = detail.StateDetails.ShouldNotBeNull();
        stateDetails.ShouldSatisfyAllConditions(
            () => detail.State.ShouldBe("publish_failed"),
            () => stateDetails.WorkerRunId.ShouldNotBeNull(),
            () => stateDetails.BranchName.ShouldBe("feat/1-test"),
            () => stateDetails.FailureReason.ShouldBe("remote unreachable"),
            () => stateDetails.FailedAt.ShouldNotBeNull(),
            () => stateDetails.BranchUrl.ShouldBeNull(),
            () => stateDetails.SideBranchName.ShouldBeNull(),
            () => stateDetails.SideBranchUrl.ShouldBeNull());
    }

    [Fact]
    public async Task WhenRevisionPublishFailed_ReturnsTheRevisionTriggerKind()
    {
        // Arrange
        Issue publishFailed = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .PublishFailedFromRevision();

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(publishFailed);

        // Assert
        detail.StateDetails.ShouldNotBeNull().RevisionTriggerKind.ShouldNotBeNull();
    }
}
