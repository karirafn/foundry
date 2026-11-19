using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Registers a repository's webhook on the provider — create, or update in place when a hook carrying
/// the repository's hook id exists, deleting any duplicates — with the account token (ADR 0085,
/// ADR 0115, ADR 0116).
/// </summary>
/// <remarks>
/// No provider call is made when webhook ingress is not configured or the account token is absent or
/// unreadable. A successful registration moves the Webhook Status to <c>Pending</c> (an
/// <c>Active</c> hook stays active); a failure leaves it unchanged, so a <c>NotRegistered</c>
/// repository keeps showing the manual instructions. The caller saves the repository afterwards — a
/// first registration may also generate the webhook secret.
/// </remarks>
internal sealed class WebhookRegistrar(
    WebhookIngress webhookIngress,
    IIssueProviderFactory providerFactory,
    ILogger<WebhookRegistrar> logger)
{
    internal const string UnsupportedProviderDetail = "Webhooks can only be registered on GitHub and GitLab.";

    internal const string UnreachableDetail = "The provider could not be reached while registering the webhook.";

    /// <summary>
    /// Creates or updates the repository's hook at the current payload URL. Any provider hook whose URL
    /// path carries the repository's hook id is adopted, whatever its base URL.
    /// </summary>
    public async Task<WebhookRegistrationOutcome> RegisterAsync(
        MonitoredRepository repository,
        Credential account,
        CancellationToken cancellationToken)
    {
        WebhookIngressState ingress = await webhookIngress.GetStateAsync(cancellationToken);
        if (!ingress.IsConfigured)
        {
            return Skipped(WebhookRegistrationOutcome.NotConfiguredReason);
        }

        if (account.ReadableTokenValue is not { } accountToken)
        {
            return Skipped(WebhookRegistrationOutcome.TokenUnreadableReason);
        }

        if (WebhookProviders.From(account) is not { } provider)
        {
            return Failed(WebhookRegistrationOutcome.ProviderErrorReason, UnsupportedProviderDetail);
        }

        string payloadUrl = ingress.PayloadUrl(provider, repository.WebhookHookId)!;

        WebhookHookSpec spec = new(
            payloadUrl,
            repository.EnsureWebhookSecret(),
            WebhookHookPath.For(provider, repository.WebhookHookId));

        Result<WebhookUpsertKind> result = await UpsertAsync(repository.Slug, account, accountToken, spec, cancellationToken);

        switch (result)
        {
            case Result<WebhookUpsertKind>.Success success:
                repository.MarkWebhookRegistered();
                logger.LogInformation(
                    "Webhook for repository {Slug} {Action} on the provider.",
                    repository.Slug,
                    success.Value == WebhookUpsertKind.Created ? "created" : "updated");
                return new WebhookRegistrationOutcome(success.Value == WebhookUpsertKind.Created
                    ? WebhookRegistrationOutcome.CreatedKind
                    : WebhookRegistrationOutcome.UpdatedKind);

            case Result<WebhookUpsertKind>.Failure failure:
                string reason = failure.Error.Code == WebhookWriteErrors.InsufficientPermissionCode
                    ? WebhookRegistrationOutcome.InsufficientPermissionReason
                    : WebhookRegistrationOutcome.ProviderErrorReason;
                logger.LogWarning(
                    "Registering the webhook for repository {Slug} failed ({ErrorCode}).",
                    repository.Slug,
                    failure.Error.Code);
                return Failed(reason, failure.Error.Message);

            default:
                throw new InvalidOperationException("Unexpected result.");
        }
    }

    private async Task<Result<WebhookUpsertKind>> UpsertAsync(
        RepositorySlug slug,
        Credential account,
        string accountToken,
        WebhookHookSpec spec,
        CancellationToken cancellationToken)
    {
        try
        {
            IIssueProvider provider = providerFactory.CreateProvider(account, accountToken);
            return await provider.UpsertWebhookAsync(slug, spec, cancellationToken);
        }
#pragma warning disable CA1031 // Provider calls may fail with any exception type (network, serialization, etc.) — report as a provider error
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Registering the webhook for repository {Slug} threw.", slug);
            return Result<WebhookUpsertKind>.Fail(new Error("Webhooks.ProviderError", UnreachableDetail));
        }
    }

    private static WebhookRegistrationOutcome Skipped(string reason) =>
        new(WebhookRegistrationOutcome.SkippedKind, reason);

    private static WebhookRegistrationOutcome Failed(string reason, string detail) =>
        new(WebhookRegistrationOutcome.FailedKind, reason, detail);
}
