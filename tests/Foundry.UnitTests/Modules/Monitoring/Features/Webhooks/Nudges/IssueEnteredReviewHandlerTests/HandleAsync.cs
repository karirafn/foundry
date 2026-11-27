using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.Nudges.IssueEnteredReviewHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenIssueEntersReview_EnqueuesAnImmediateReviewNudge()
    {
        // Arrange
        WebhookNudgeQueue queue = new(NullLogger<WebhookNudgeQueue>.Instance);
        IssueEnteredReviewHandler handler = new(queue);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        await handler.HandleAsync(new IssueEnteredReview(repositoryId, 42), TestContext.Current.CancellationToken);

        // Assert
        queue.Reader.TryRead(out Nudge nudge).ShouldBeTrue();
        nudge.ShouldBe(new Nudge(new NudgeKey(repositoryId, NudgePass.Review, 42)));
        queue.Reader.TryRead(out _).ShouldBeFalse();
    }
}
