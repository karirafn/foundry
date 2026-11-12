using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.IssueReactions;

/// <summary>
/// Writes the blockers triage named (ADR 0103): links each on the provider, then posts one comment. Redelivery
/// re-adds links the provider already holds, which count as success. A transient failure throws so the relay
/// retries; a permanent link refusal is reported as <see cref="BlockerLinkFailed"/> and stops the writes, while a
/// refused comment only logs, since the links are already in place.
/// </summary>
[IntegrationEventHandlerIdentity("Monitoring.TriageBlockersFound")]
internal sealed class TriageBlockersFoundHandler(
    BlockerLinkWriter writer,
    IIntegrationEventDispatcher integrationEvents,
    TimeProvider timeProvider,
    ILogger<TriageBlockersFoundHandler> logger)
    : IIntegrationEventHandler<TriageBlockersFound>
{
    public async Task HandleAsync(TriageBlockersFound @event, CancellationToken cancellationToken)
    {
        foreach (TriageBlocker blocker in @event.Blockers)
        {
            Result linked = await writer.AddBlockerAsync(
                @event.MonitoredRepositoryId, @event.IssueNumber, blocker.IssueNumber, cancellationToken);

            if (linked is not Result.Failure failure)
            {
                continue;
            }

            if (BlockerLinkErrors.IsTransient(failure.Error))
            {
                throw new InvalidOperationException(failure.Error.Message);
            }

            await ReportRefusedLinkAsync(@event, blocker, failure.Error, cancellationToken);
            return;
        }

        Result commented = await writer.PostIssueCommentAsync(
            @event.MonitoredRepositoryId,
            @event.IssueNumber,
            TriageBlockerComment.Compose(@event.Blockers),
            cancellationToken);

        if (commented is not Result.Failure commentFailure)
        {
            return;
        }

        if (CommentPostErrors.IsTransient(commentFailure.Error))
        {
            throw new InvalidOperationException(commentFailure.Error.Message);
        }

        logger.LogWarning(
            "Triage comment on issue {IssueNumber} of repository {RepositoryId} was refused: {Message}",
            @event.IssueNumber,
            @event.MonitoredRepositoryId.Value,
            commentFailure.Error.Message);
    }

    private Task ReportRefusedLinkAsync(
        TriageBlockersFound @event,
        TriageBlocker blocker,
        Error error,
        CancellationToken cancellationToken) =>
        integrationEvents.DispatchAsync(
            [
                new BlockerLinkFailed(
                    @event.MonitoredRepositoryId,
                    @event.IssueNumber,
                    @event.WorkerRunId.Value,
                    blocker.IssueNumber,
                    TriageBlockerComment.Flatten(error.Message),
                    timeProvider.GetUtcNow()),
            ],
            cancellationToken);
}
