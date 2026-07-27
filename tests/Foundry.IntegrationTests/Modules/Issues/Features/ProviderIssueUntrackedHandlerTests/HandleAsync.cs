using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.ProviderIssueUntrackedHandlerTests;

// Handler has no HTTP endpoint — dispatched only through the integration-event pipeline.
// These tests resolve the handler from DI and call it directly against the real wired SQLite context.
public sealed class HandleAsync : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public HandleAsync()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<DetectedIssue> SeedDetectedIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        dbContext.Set<Issue>().Add(detected);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return detected;
    }

    private async Task<CompletedIssue> SeedCompletedIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: "Issue title",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            inProgress.WorkerRunId,
            "feat/1-fix",
            "https://github.com/owner/repo/pull/10",
            DateTimeOffset.UtcNow);
        CompletedIssue completed = review.Complete(DateTimeOffset.UtcNow);

        dbContext.Set<Issue>().Add(completed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return completed;
    }

    [Fact]
    public async Task WhenRestingStateIssue_DeletesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = await SeedDetectedIssueAsync(repositoryId, issueNumber: 1);
        ProviderIssueUntracked @event = new(repositoryId, IssueNumber: 1);

        // Act
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<ProviderIssueUntracked> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<ProviderIssueUntracked>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? issue = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == detected.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeNull();
    }

    [Fact]
    public async Task WhenPreservedStateIssue_PreservesRecord()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        CompletedIssue completed = await SeedCompletedIssueAsync(repositoryId, issueNumber: 2);
        ProviderIssueUntracked @event = new(repositoryId, IssueNumber: 2);

        // Act
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<ProviderIssueUntracked> handler =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<ProviderIssueUntracked>>();
        await handler.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        using IServiceScope assertScope = _factory.Services.CreateScope();
        DbContext dbContext = assertScope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? issue = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == completed.Id,
                TestContext.Current.CancellationToken);
        issue.ShouldBeOfType<CompletedIssue>();
    }
}
