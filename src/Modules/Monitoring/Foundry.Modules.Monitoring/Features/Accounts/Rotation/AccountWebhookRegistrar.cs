using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Accounts.Rotation;

/// <summary>
/// Registers the webhook of every repository an account covers whose Webhook Status is
/// <see cref="WebhookStatus.NotRegistered"/>, after the account's server-side (branch-rules) token is
/// set or replaced.
/// </summary>
/// <remarks>
/// Repositories are processed sequentially — the DbContext is not thread-safe and the provider
/// writes must not run in parallel. A failure on one repository is logged and never stops the
/// others or fails the account update, which is already committed before this runs.
/// </remarks>
internal sealed class AccountWebhookRegistrar(
    DbContext dbContext,
    RepositoryEligibilityDiffer differ,
    WebhookRegistrar registrar,
    ILogger<AccountWebhookRegistrar> logger)
{
    public async Task RegisterNotRegisteredAsync(Credential credential, CancellationToken cancellationToken)
    {
        List<MonitoredRepository> notRegistered = (await differ.FindResolvingReposAsync(credential, cancellationToken))
            .Where(r => r.WebhookStatus == WebhookStatus.NotRegistered)
            .ToList();

        foreach (MonitoredRepository repository in notRegistered)
        {
            try
            {
                WebhookRegistrationOutcome outcome = await registrar.RegisterAsync(repository, credential, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "Webhook for repository {Slug} after the server-side token was saved: {Kind} ({Reason}).",
                    repository.Slug,
                    outcome.Kind,
                    outcome.Reason);
            }
#pragma warning disable CA1031 // One repository's failure must never stop the others or fail the account update
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogError(
                    ex,
                    "Registering the webhook for repository {Slug} after the server-side token was saved threw.",
                    repository.Slug);
            }
        }
    }
}
