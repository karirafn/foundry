using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Turns a verified push delivery (GitHub <c>push</c>, GitLab Push Hook) into the ephemeral
/// <see cref="ProviderBranchPushed"/> event (issue #660). The relevance filter keeps only pushes to the
/// branch of a running worker; everything else — the default branch, tags, a branch whose run is not yet
/// recorded as running — is dropped. The event is delivered directly via
/// <see cref="IIntegrationEventProcessor"/>, not the outbox: it only nudges a display-only commit count.
/// </summary>
internal sealed class BranchPushPublisher(
    IRunningBranchQueries runningBranchQueries,
    IIntegrationEventProcessor integrationEventProcessor,
    ILogger<BranchPushPublisher> logger)
{
    private const string BranchRefPrefix = "refs/heads/";

    /// <summary>
    /// The branch a push payload's <c>ref</c> names, or <see langword="null"/> when it is missing or not a
    /// branch (a tag push). GitHub and GitLab share the <c>ref</c> shape.
    /// </summary>
    internal static string? GetPushedBranch(JsonElement payload)
    {
        string? pushedRef = WebhookRelevance.GetString(payload, "ref");
        if (pushedRef is null || !pushedRef.StartsWith(BranchRefPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string branch = pushedRef[BranchRefPrefix.Length..];
        return string.IsNullOrWhiteSpace(branch) ? null : branch;
    }

    /// <summary>
    /// Publishes <see cref="ProviderBranchPushed"/> when <paramref name="payload"/> pushes the branch of a
    /// running worker of <paramref name="repositoryId"/>. Returns whether the event was published.
    /// </summary>
    public async Task<bool> PublishIfRelevantAsync(
        MonitoredRepositoryId repositoryId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (GetPushedBranch(payload) is not { } branch
            || !await runningBranchQueries.IsRunningBranchAsync(repositoryId.Value, branch, cancellationToken))
        {
            return false;
        }

        try
        {
            await integrationEventProcessor.ProcessAsync(
                Guid.NewGuid(),
                new ProviderBranchPushed(repositoryId, branch),
                cancellationToken);
            return true;
        }
#pragma warning disable CA1031 // A failed nudge must not fail the delivery; the next push or the container exit corrects the count.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to deliver {EventType} for repository {RepositoryId} directly.",
                nameof(ProviderBranchPushed),
                repositoryId.Value);
            return false;
        }
    }
}
