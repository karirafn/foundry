using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Publishes what a probe pass committed: tells open Settings pages to read the package source list again, then
/// dispatches the sources' domain events (re-evaluating the repositories that declare a source whose readability
/// changed).
/// </summary>
internal sealed class PackageSourceVerdictPublisher(
    IDomainEventDispatcher domainEventDispatcher,
    ISystemNotificationBroadcaster broadcaster,
    ILogger<PackageSourceVerdictPublisher> logger)
{
    /// <summary>
    /// Sends the Settings notification when <paramref name="notifySettings"/>, then dispatches
    /// <paramref name="committedEvents"/>, which must come only from saved verdicts.
    /// </summary>
    public async Task PublishAsync(
        IReadOnlyCollection<IDomainEvent> committedEvents,
        bool notifySettings,
        CancellationToken cancellationToken)
    {
        if (notifySettings)
        {
            await NotifySettingsAsync(cancellationToken);
        }

        if (committedEvents.Count > 0)
        {
            await domainEventDispatcher.DispatchAsync(committedEvents, cancellationToken);
        }
    }

    private async Task NotifySettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await broadcaster.SendAsync(
                new SystemNotification(PackageSourceNotification.Category, IsActive: false, Message: string.Empty),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The verdicts are committed; an open page still refetches on reconnect or after its pending-check exit.
            logger.LogWarning(ex, "Could not broadcast the package source notification.");
        }
    }
}
