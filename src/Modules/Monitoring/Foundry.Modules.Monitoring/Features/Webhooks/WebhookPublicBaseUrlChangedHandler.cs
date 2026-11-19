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
/// Registers every monitored repository's webhook at the new payload URL after the Webhook Public Base
/// URL changes, <c>NotRegistered</c> repositories included. A hook at any earlier base URL carries the
/// repository's hook id, so it is updated in place rather than duplicated (ADR 0116).
/// </summary>
/// <remarks>
/// Repositories are processed sequentially and one failure never stops the rest. Each registration
/// is a create-or-update keyed by hook id, so a redelivery after a partial run is harmless.
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
        List<MonitoredRepository> repositories = await dbContext.Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(cancellationToken);

        foreach (MonitoredRepository repository in repositories)
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
