using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Deletes every provider hook carrying a removed repository's hook id, with the covering account's
/// token (ADR 0116).
/// </summary>
/// <remarks>
/// Best effort: no covering account, an unreadable token, a refused or failed provider call, or an
/// exception is logged and never blocks the removal. A hook left behind delivers to an unknown hook id
/// and is answered 404.
/// </remarks>
internal sealed class RepositoryWebhookDeleter(
    ICredentialResolver credentialResolver,
    IIssueProviderFactory providerFactory,
    ILogger<RepositoryWebhookDeleter> logger)
{
    public async Task DeleteAsync(MonitoredRepository repository, CancellationToken cancellationToken)
    {
        try
        {
            Credential? account = await credentialResolver.ResolveAsync(
                repository.Host,
                repository.Slug,
                cancellationToken);
            if (account is null)
            {
                logger.LogWarning(
                    "No account covers repository {Slug}; its webhooks were not deleted.",
                    repository.Slug);
                return;
            }

            if (account.ReadableTokenValue is not { } accountToken)
            {
                logger.LogWarning(
                    "The account token for repository {Slug} cannot be read; its webhooks were not deleted.",
                    repository.Slug);
                return;
            }

            IIssueProvider provider = providerFactory.CreateProvider(account, accountToken);
            Result<int> result = await provider.DeleteWebhooksAsync(
                repository.Slug,
                repository.WebhookHookId,
                cancellationToken);

            switch (result)
            {
                case Result<int>.Success success:
                    logger.LogInformation(
                        "Deleted {Count} webhook(s) for removed repository {Slug}.",
                        success.Value,
                        repository.Slug);
                    break;

                case Result<int>.Failure failure:
                    logger.LogWarning(
                        "Deleting the webhooks for removed repository {Slug} failed ({ErrorCode}): {Message}",
                        repository.Slug,
                        failure.Error.Code,
                        failure.Error.Message);
                    break;
            }
        }
#pragma warning disable CA1031 // Provider calls may fail with any exception type; a failed hook deletion never blocks the removal
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Deleting the webhooks for removed repository {Slug} threw.", repository.Slug);
        }
    }
}
