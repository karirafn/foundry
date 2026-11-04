using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.IssueEnteredChecksPendingHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenIssueEntersChecksPending_EnqueuesImmediateAndDeadlineChecksNudges()
    {
        // Arrange
        WebhookNudgeQueue queue = new(NullLogger<WebhookNudgeQueue>.Instance);
        IssueEnteredChecksPendingHandler handler = new(queue);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DateTimeOffset entered = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

        // Act
        await handler.HandleAsync(
            new IssueEnteredChecksPending(repositoryId, 42, entered),
            TestContext.Current.CancellationToken);

        // Assert
        List<Nudge> nudges = [];
        while (queue.Reader.TryRead(out Nudge nudge))
        {
            nudges.Add(nudge);
        }

        NudgeKey key = new(repositoryId, NudgePass.Checks, 42);
        nudges.ShouldBe([
            new Nudge(key),
            new Nudge(key, entered + RepositoryPoller.ChecksNoneGraceWindow),
            new Nudge(key, entered + RepositoryPoller.ChecksPendingUpperBound),
        ]);
    }
}
