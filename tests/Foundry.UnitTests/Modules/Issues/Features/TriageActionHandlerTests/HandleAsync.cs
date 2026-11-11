using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.TriageActionHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher = new();
    private readonly StubIssueQueries _issueQueries = new();

    public HandleAsync()
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

    private async Task<TIssue> SeedAsync<TIssue>(TIssue issue)
        where TIssue : Issue
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private async Task<Issue> ReloadAsync(Issue issue)
    {
        _dbContext.ChangeTracker.Clear();
        Issue? reloaded = await _dbContext.Set<Issue>().FindAsync([issue.Id], TestContext.Current.CancellationToken);
        return reloaded.ShouldNotBeNull();
    }

    [Fact]
    public async Task RetryTriage_FromTriageFailed_QueuesTriage()
    {
        // Arrange
        TriageFailedIssue failed = await SeedAsync(new IssueBuilder().TriageFailed());
        RetryTriage.Handler sut = new(_dbContext, _dispatcher, _issueQueries);

        // Act
        Result<IssueDetail> result = await sut.HandleAsync(new RetryTriage.Command(failed.Id), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        (await ReloadAsync(failed)).ShouldBeOfType<TriageQueuedIssue>();
    }

    [Fact]
    public async Task RetryTriage_FromAnyOtherState_IsAWrongStateError()
    {
        // Arrange
        FailedIssue failed = await SeedAsync(new IssueBuilder().Failed());
        RetryTriage.Handler sut = new(_dbContext, _dispatcher, _issueQueries);

        // Act
        Result<IssueDetail> result = await sut.HandleAsync(new RetryTriage.Command(failed.Id), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.WrongStateCode);
    }

    [Fact]
    public async Task SkipTriage_FromTriageFailed_QueuesTheIssueWithItsKindAsItIs()
    {
        // Arrange
        TriageFailedIssue failed = await SeedAsync(new IssueBuilder()
            .WithKindClassification(KindClassification.Unclassified)
            .TriageFailed());
        SkipTriage.Handler sut = new(_dbContext, _dispatcher, _issueQueries);

        // Act
        Result<IssueDetail> result = await sut.HandleAsync(new SkipTriage.Command(failed.Id), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        FreshQueuedIssue queued = (await ReloadAsync(failed)).ShouldBeOfType<FreshQueuedIssue>();
        queued.KindClassification.ShouldBe(KindClassification.Unclassified);
    }

    [Fact]
    public async Task SkipTriage_FromAnyOtherState_IsAWrongStateError()
    {
        // Arrange
        TriageQueuedIssue queued = await SeedAsync(new IssueBuilder().TriageQueued());
        SkipTriage.Handler sut = new(_dbContext, _dispatcher, _issueQueries);

        // Act
        Result<IssueDetail> result = await sut.HandleAsync(new SkipTriage.Command(queued.Id), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.WrongStateCode);
    }

    [Fact]
    public async Task Retriage_FromQueued_QueuesTriage()
    {
        // Arrange
        FreshQueuedIssue queued = await SeedAsync(new IssueBuilder().FreshQueued());
        Retriage.Handler sut = new(_dbContext, _dispatcher, _issueQueries);

        // Act
        Result<IssueDetail> result = await sut.HandleAsync(new Retriage.Command(queued.Id), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        (await ReloadAsync(queued)).ShouldBeOfType<TriageQueuedIssue>();
    }

    [Fact]
    public async Task Retriage_FromBlocked_QueuesTriageKeepingItsBlockers()
    {
        // Arrange
        BlockedIssue blocked = await SeedAsync(new IssueBuilder().Detected().Block([2]));
        Retriage.Handler sut = new(_dbContext, _dispatcher, _issueQueries);

        // Act
        Result<IssueDetail> result = await sut.HandleAsync(new Retriage.Command(blocked.Id), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        (await ReloadAsync(blocked)).ShouldBeOfType<TriageQueuedIssue>().BlockedBy.ShouldBe([2]);
    }

    [Fact]
    public async Task Retriage_FromAnyOtherState_IsAWrongStateError()
    {
        // Arrange
        InProgressIssue inProgress = await SeedAsync(new IssueBuilder().InProgress());
        Retriage.Handler sut = new(_dbContext, _dispatcher, _issueQueries);

        // Act
        Result<IssueDetail> result = await sut.HandleAsync(new Retriage.Command(inProgress.Id), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.WrongStateCode);
    }
}
