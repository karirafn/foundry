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

public sealed class GetIssueDetailAsync_RunKind : IAsyncDisposable
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetIssueDetailAsync_RunKind()
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

    [Fact]
    public async Task WhileAFreshRunIsInProgress_TheDetailReportsTheFreshRunKind()
    {
        // Arrange
        Issue issue = new IssueBuilder().WithMonitoredRepositoryId(RepositoryId).InProgress();

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(issue);

        // Assert
        detail.RunKind.ShouldBe(IssueSummary.FreshRunKind);
    }

    // A run publishes while the issue is still in progress, so its summary comment can name the run's kind.
    [Fact]
    public async Task WhileARevisionRunIsInProgress_TheDetailReportsTheRevisionsRunKind()
    {
        // Arrange
        Issue issue = new IssueBuilder().WithMonitoredRepositoryId(RepositoryId).RevisionInProgress();

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(issue);

        // Assert
        detail.RunKind.ShouldBe(IssueSummary.ReviewFixRunKind);
    }

    [Fact]
    public async Task WhenNoRunIsQueuedOrRunning_TheDetailReportsNoRunKind()
    {
        // Arrange
        Issue issue = new IssueBuilder().WithMonitoredRepositoryId(RepositoryId).Completed();

        // Act
        IssueDetail detail = await SaveAndGetDetailAsync(issue);

        // Assert
        detail.RunKind.ShouldBeNull();
    }
}
