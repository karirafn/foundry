using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.Nudges.IssueEnteredTriageQueueHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenIssueEntersTriageQueue_EnqueuesAnImmediateDependencyNudgeForItsRepository()
    {
        // Arrange
        WebhookNudgeQueue queue = new(NullLogger<WebhookNudgeQueue>.Instance);
        IssueEnteredTriageQueueHandler handler = new(queue);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        await handler.HandleAsync(new IssueEnteredTriageQueue(repositoryId), TestContext.Current.CancellationToken);

        // Assert
        queue.Reader.TryRead(out Nudge nudge).ShouldBeTrue();
        nudge.ShouldBe(new Nudge(new NudgeKey(repositoryId, NudgePass.Dependencies)));
        queue.Reader.TryRead(out _).ShouldBeFalse();
    }
}
