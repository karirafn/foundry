using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
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

public sealed class GetIssueDetailAsync_TriageBlocked : IAsyncDisposable
{
    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetIssueDetailAsync_TriageBlocked()
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

    [Fact]
    public async Task WhenTriageBlocked_ServesTheStateNameAndItsBlockers()
    {
        // Arrange
        Issue blocked = new IssueBuilder().WithMonitoredRepositoryId(RepositoryId).TriageBlocked([3, 5]);
        _dbContext.Set<Issue>().Add(blocked);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(blocked.Id, TestContext.Current.CancellationToken);

        // Assert
        IssueDetail detail = result.ShouldBeOfType<Result<IssueDetail>.Success>().Value;
        detail.ShouldSatisfyAllConditions(
            () => detail.State.ShouldBe("triage_blocked"),
            () => detail.StateDetails.ShouldNotBeNull().BlockedBy.ShouldBe([3, 5]));
    }
}
