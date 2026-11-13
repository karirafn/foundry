using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.BranchDivergence;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.BranchDivergence.BranchDivergedResumeServiceTests;

public sealed class TickAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly StubRunPublishingCommands _publishing = new();

    public TickAsync()
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

    private BranchDivergedIssue SeedDiverged(int issueNumber, string branchName, string sideBranchName)
    {
        BranchDivergedIssue diverged = new IssueBuilder()
            .WithIssueNumber(issueNumber)
            .WithBranchName(branchName)
            .BranchDiverged(sideBranchName);
        _dbContext.Set<Issue>().Add(diverged);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return diverged;
    }

    private BranchDivergedResumeService BuildSut()
    {
        ServiceCollection services = new();
        services.AddDbContext<FoundryDbContext>(opts => opts.UseSqlite(_connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddScoped<IDomainEventDispatcher>(_ => new CapturingDomainEventDispatcher());
        services.AddScoped<IRunPublishingCommands>(_ => _publishing);
        ServiceProvider sp = services.BuildServiceProvider();

        return new BranchDivergedResumeService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<BranchDivergedResumeService>.Instance);
    }

    private async Task<Issue> ReloadAsync(Issue issue)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .SingleAsync(i => i.Id == issue.Id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenSideBranchWasMergedIntoTheIssueBranch_ResumesAsAContinuation()
    {
        // Arrange
        BranchDivergedIssue diverged = SeedDiverged(1, "feat/1-a", "feat/1-a-run-x");
        _publishing.Answer("feat/1-a-run-x", "feat/1-a", new Result<bool>.Success(true));

        // Act
        await BuildSut().TickForTest(TestContext.Current.CancellationToken);

        // Assert
        ContinuationQueuedIssue queued = (await ReloadAsync(diverged)).ShouldBeOfType<ContinuationQueuedIssue>();
        queued.BranchName.ShouldBe("feat/1-a");
        _publishing.Calls.ShouldHaveSingleItem().ShouldBe((diverged.MonitoredRepositoryId, "feat/1-a-run-x", "feat/1-a"));
    }

    [Fact]
    public async Task WhenSideBranchIsNotMergedYet_LeavesTheIssueDiverged()
    {
        // Arrange
        BranchDivergedIssue diverged = SeedDiverged(1, "feat/1-a", "feat/1-a-run-x");
        _publishing.Answer("feat/1-a-run-x", "feat/1-a", new Result<bool>.Success(false));

        // Act
        await BuildSut().TickForTest(TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(diverged)).ShouldBeOfType<BranchDivergedIssue>();
    }

    [Fact]
    public async Task WhenTheProviderCheckFails_LeavesTheIssueDivergedAndChecksTheOthers()
    {
        // Arrange
        BranchDivergedIssue failing = SeedDiverged(1, "feat/1-a", "feat/1-a-run-x");
        BranchDivergedIssue merged = SeedDiverged(2, "feat/2-b", "feat/2-b-run-y");
        _publishing.Answer("feat/1-a-run-x", "feat/1-a", Result<bool>.Fail(new Error("Provider.NotFound", "Branch not found")));
        _publishing.Answer("feat/2-b-run-y", "feat/2-b", new Result<bool>.Success(true));

        // Act
        await BuildSut().TickForTest(TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(failing)).ShouldBeOfType<BranchDivergedIssue>();
        (await ReloadAsync(merged)).ShouldBeOfType<ContinuationQueuedIssue>();
    }

    [Fact]
    public async Task WhenTheProviderCheckThrows_LeavesTheIssueDivergedAndChecksTheOthers()
    {
        // Arrange
        BranchDivergedIssue throwing = SeedDiverged(1, "feat/1-a", "feat/1-a-run-x");
        BranchDivergedIssue merged = SeedDiverged(2, "feat/2-b", "feat/2-b-run-y");
        _publishing.Throw("feat/1-a-run-x");
        _publishing.Answer("feat/2-b-run-y", "feat/2-b", new Result<bool>.Success(true));

        // Act
        await BuildSut().TickForTest(TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(throwing)).ShouldBeOfType<BranchDivergedIssue>();
        (await ReloadAsync(merged)).ShouldBeOfType<ContinuationQueuedIssue>();
    }

    [Fact]
    public void TickInterval_IsOneMinute()
    {
        // Arrange
        // (the constant is the system under test)

        // Act
        TimeSpan interval = BranchDivergedResumeService.CheckInterval;

        // Assert
        interval.ShouldBe(TimeSpan.FromMinutes(1));
    }

    private sealed class StubRunPublishingCommands : IRunPublishingCommands
    {
        private readonly Dictionary<(string Ancestor, string Branch), Result<bool>> _answers = [];
        private readonly HashSet<string> _throwing = new(StringComparer.Ordinal);

        public List<(MonitoredRepositoryId RepositoryId, string Ancestor, string Branch)> Calls { get; } = [];

        public void Answer(string ancestorRef, string branchName, Result<bool> answer) =>
            _answers[(ancestorRef, branchName)] = answer;

        public void Throw(string ancestorRef) => _throwing.Add(ancestorRef);

        public Task<Result<bool>> IsAncestorOfBranchAsync(
            MonitoredRepositoryId repositoryId,
            string ancestorRef,
            string branchName,
            CancellationToken cancellationToken)
        {
            Calls.Add((repositoryId, ancestorRef, branchName));
            if (_throwing.Contains(ancestorRef))
            {
                throw new HttpRequestException("provider unreachable");
            }

            return Task.FromResult(_answers[(ancestorRef, branchName)]);
        }

        public Task<Result<string>> OpenPullRequestAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            string title,
            string body,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> PostRunSummaryAsync(
            MonitoredRepositoryId repositoryId,
            string pullRequestUrl,
            string body,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<CommitIdentity>> GetCommitIdentityAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
