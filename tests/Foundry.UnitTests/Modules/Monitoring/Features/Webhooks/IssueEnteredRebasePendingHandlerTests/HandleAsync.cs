using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.IssueEnteredRebasePendingHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenIssueEntersRebasePending_EnqueuesAnImmediateRebaseNudge()
    {
        // Arrange
        WebhookNudgeQueue queue = new(NullLogger<WebhookNudgeQueue>.Instance);
        IssueEnteredRebasePendingHandler handler = new(queue);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        await handler.HandleAsync(
            new IssueEnteredRebasePending(repositoryId, 42, new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)),
            TestContext.Current.CancellationToken);

        // Assert
        queue.Reader.TryRead(out Nudge nudge).ShouldBeTrue();
        nudge.ShouldBe(new Nudge(new NudgeKey(repositoryId, NudgePass.Rebase, 42)));
        queue.Reader.TryRead(out _).ShouldBeFalse();
    }
}
