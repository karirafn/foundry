using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.IssueReactions;

/// <summary>
/// Posts the details a ready triage verdict settled itself on the issue thread (ADR 0123), registered as Foundry's own
/// so it is never read back as input. The issue has already moved on, so a transient failure throws for the relay to
/// retry and a refused comment only logs.
/// </summary>
[IntegrationEventHandlerIdentity("Monitoring.TriageDecisionsRecorded")]
internal sealed class TriageDecisionsRecordedHandler(
    BlockerLinkWriter writer,
    ILogger<TriageDecisionsRecordedHandler> logger)
    : IIntegrationEventHandler<TriageDecisionsRecorded>
{
    public async Task HandleAsync(TriageDecisionsRecorded @event, CancellationToken cancellationToken)
    {
        Result commented = await writer.PostIssueCommentAsync(
            @event.MonitoredRepositoryId,
            @event.IssueNumber,
            TriageDecisionComment.Compose(@event.Decisions, @event.Release),
            cancellationToken);

        if (commented is not Result.Failure failure)
        {
            return;
        }

        if (CommentPostErrors.IsTransient(failure.Error))
        {
            throw new InvalidOperationException(failure.Error.Message);
        }

        logger.LogWarning(
            "Triage decision comment on issue {IssueNumber} of repository {RepositoryId} was refused: {Message}",
            @event.IssueNumber,
            @event.MonitoredRepositoryId.Value,
            failure.Error.Message);
    }
}
