using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Settings.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Re-points every registered repository webhook (any status other than <c>NotRegistered</c>) at the
/// new payload URL after the Webhook Public Base URL changes. A hook still at the previous URL is
/// updated in place rather than duplicated.
/// </summary>
/// <remarks>
/// Repositories are processed sequentially and one failure never stops the rest. Each registration
/// is a create-or-update keyed by payload URL, so a redelivery after a partial run is harmless.
/// </remarks>
[IntegrationEventHandlerIdentity("Monitoring.WebhookPublicBaseUrlChanged")]
internal sealed class WebhookPublicBaseUrlChangedHandler(
    DbContext dbContext,
    ICredentialResolver credentialResolver,
    WebhookRegistrar registrar,
    ILogger<WebhookPublicBaseUrlChangedHandler> logger)
    : IIntegrationEventHandler<WebhookPublicBaseUrlChanged>
{
    public async Task HandleAsync(WebhookPublicBaseUrlChanged @event, CancellationToken cancellationToken)
    {
        List<MonitoredRepository> registered = await dbContext.Set<MonitoredRepository>()
            .Where(r => r.WebhookStatus != WebhookStatus.NotRegistered)
            .OrderBy(r => r.Position)
            .ToListAsync(cancellationToken);

        foreach (MonitoredRepository repository in registered)
        {
            try
            {
                Credential? credential = await credentialResolver.ResolveAsync(
                    repository.Host,
                    repository.Slug,
                    cancellationToken);

                if (credential is null)
                {
                    logger.LogDebug(
                        "No credential covers repository {Slug}; its webhook was not re-pointed.",
                        repository.Slug);
                    continue;
                }

                WebhookRegistrationOutcome outcome = await registrar.RegisterAsync(
                    repository,
                    credential,
                    @event.PreviousPublicBaseUrl,
                    cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "Webhook for repository {Slug} after the public base URL changed: {Kind} ({Reason}).",
                    repository.Slug,
                    outcome.Kind,
                    outcome.Reason);
            }
#pragma warning disable CA1031 // One repository's failure must never stop the rest
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogError(
                    ex,
                    "Re-pointing the webhook for repository {Slug} after the public base URL changed threw.",
                    repository.Slug);
            }
        }
    }
}
