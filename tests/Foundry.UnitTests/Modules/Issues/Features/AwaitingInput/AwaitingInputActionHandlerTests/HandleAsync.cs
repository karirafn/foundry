using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.AwaitingInput;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Issues.Features.TriageActionHandlerTests;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.AwaitingInput.AwaitingInputActionHandlerTests;

/// <summary>The operator actions on an awaiting-input issue (ADR 0096): Continue without answer and Retry post.</summary>
public sealed class HandleAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEvents = new();
    private readonly CapturingIntegrationEventDispatcher _integrationEvents = new();
    private readonly StubIssueQueries _issueQueries = new();
    private readonly FakeTimeProvider _timeProvider = new(PostedAt.AddDays(4));

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _dbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
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

    private ContinueWithoutAnswer.Handler ContinueHandler() =>
        new(_dbContext, _domainEvents, _timeProvider, _issueQueries);

    private RetryQuestionPost.Handler RetryPostHandler() =>
        new(_dbContext, _integrationEvents, _issueQueries);

    [Fact]
    public async Task ContinueWithoutAnswer_FromAwaitingInput_ResumesWithTheRoundClosedAsUnansweredAndTheRefusalPersisted()
    {
        // Arrange
        AwaitingInputIssue awaiting = await SeedAsync(new IssueBuilder().AwaitingInputPosted(PostedAt));

        // Act
        Result<IssueDetail> result = await ContinueHandler().HandleAsync(
            new ContinueWithoutAnswer.Command(awaiting.Id),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        RevisionQueuedIssue queued = (await ReloadAsync(awaiting)).ShouldBeOfType<RevisionQueuedIssue>();
        queued.ShouldSatisfyAllConditions(
            () => queued.ContinuedWithoutAnswer.ShouldBeTrue(),
            () => queued.Clarifications.ShouldHaveSingleItem().Input.ShouldBe(ClarificationInput.ContinuedWithoutAnswer()),
            () => queued.Clarifications.ShouldHaveSingleItem().AnsweredAt.ShouldBe(_timeProvider.GetUtcNow()));
    }

    [Fact]
    public async Task ContinueWithoutAnswer_FromAnyOtherState_IsAWrongStateErrorAndChangesNothing()
    {
        // Arrange
        FreshQueuedIssue queued = await SeedAsync(new IssueBuilder().FreshQueued());

        // Act
        Result<IssueDetail> result = await ContinueHandler().HandleAsync(
            new ContinueWithoutAnswer.Command(queued.Id),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.WrongStateCode);
        FreshQueuedIssue reloaded = (await ReloadAsync(queued)).ShouldBeOfType<FreshQueuedIssue>();
        reloaded.ContinuedWithoutAnswer.ShouldBeFalse();
    }

    [Fact]
    public async Task ContinueWithoutAnswer_ForAnUnknownIssue_IsNotFound()
    {
        // Act
        Result<IssueDetail> result = await ContinueHandler().HandleAsync(
            new ContinueWithoutAnswer.Command(IssueId.New()),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.NotFoundCode);
    }

    [Fact]
    public async Task RetryPost_WhenThePostFailed_ReturnsItToPendingAndNudgesThePostStep()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInput();
        awaiting.RecordQuestionPostFailed("404", transient: false, PostedAt);
        await SeedAsync(awaiting);

        // Act
        Result<IssueDetail> result = await RetryPostHandler().HandleAsync(
            new RetryQuestionPost.Command(awaiting.Id),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Success>();
        (await ReloadAsync(awaiting)).ShouldBeOfType<AwaitingInputIssue>().QuestionPost.ShouldBeOfType<QuestionPost.Pending>();
        _integrationEvents.DispatchedEvents.ShouldHaveSingleItem()
            .ShouldBe(new IssueEnteredAwaitingInput(awaiting.MonitoredRepositoryId, awaiting.IssueNumber));
    }

    [Fact]
    public async Task RetryPost_WhenTheQuestionIsPosted_IsAWrongStateErrorAndChangesNothing()
    {
        // Arrange
        AwaitingInputIssue awaiting = await SeedAsync(new IssueBuilder().AwaitingInputPosted(PostedAt));

        // Act
        Result<IssueDetail> result = await RetryPostHandler().HandleAsync(
            new RetryQuestionPost.Command(awaiting.Id),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.WrongStateCode);
        (await ReloadAsync(awaiting)).ShouldBeOfType<AwaitingInputIssue>().QuestionPost.ShouldBeOfType<QuestionPost.Posted>();
        _integrationEvents.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task RetryPost_FromAnyOtherState_IsAWrongStateError()
    {
        // Arrange
        FailedIssue failed = await SeedAsync(new IssueBuilder().Failed());

        // Act
        Result<IssueDetail> result = await RetryPostHandler().HandleAsync(
            new RetryQuestionPost.Command(failed.Id),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IssueDetail>.Failure>().Error.Code.ShouldBe(IssueErrors.WrongStateCode);
        (await ReloadAsync(failed)).ShouldBeOfType<FailedIssue>();
    }
}
