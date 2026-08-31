using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Features.ProviderReactions.ProviderIssueUntrackedHandlerTests;

// Handler has no HTTP endpoint — dispatched only through the integration-event pipeline.
// These tests resolve the handler from DI and call it directly against the real wired SQLite context.
public sealed class HandleAsync : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

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

        DetectedIssue detected = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle("Issue title")
            .WithBody("Body")
            .WithLabels([])
            .Detected();

        dbContext.Set<Issue>().Add(detected);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return detected;
    }

    private async Task<CompletedIssue> SeedCompletedIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        CompletedIssue completed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle("Issue title")
            .WithBody("Body")
            .WithLabels([])
            .WithBranchName("feat/1-fix")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/10")
            .Completed();

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
