using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.Nudges.IssueEnteredMergePendingHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenIssueEntersMergePending_EnqueuesAnImmediateMergeNudge()
    {
        // Arrange
        WebhookNudgeQueue queue = new(NullLogger<WebhookNudgeQueue>.Instance);
        IssueEnteredMergePendingHandler handler = new(queue);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        await handler.HandleAsync(
            new IssueEnteredMergePending(repositoryId, 42, new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero)),
            TestContext.Current.CancellationToken);

        // Assert
        queue.Reader.TryRead(out Nudge nudge).ShouldBeTrue();
        nudge.ShouldBe(new Nudge(new NudgeKey(repositoryId, NudgePass.Merge, 42)));
        queue.Reader.TryRead(out _).ShouldBeFalse();
    }
}
