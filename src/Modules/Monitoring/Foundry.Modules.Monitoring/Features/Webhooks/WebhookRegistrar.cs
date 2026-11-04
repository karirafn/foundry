using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Registers a repository's webhook on the provider — create, or update in place when a hook with the
/// same payload URL exists — with the account's server-side (branch-rules) token, which is never
/// injected into workers (ADR 0084, ADR 0085). The worker token is never used.
/// </summary>
/// <remarks>
/// No provider call is made when webhook ingress is not configured or the account has no readable
/// server-side token. A successful registration moves the Webhook Status to <c>Pending</c> (an
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

    /// <summary>Creates or updates the repository's hook at the current payload URL.</summary>
    public Task<WebhookRegistrationOutcome> RegisterAsync(
        MonitoredRepository repository,
        Credential account,
        CancellationToken cancellationToken)
        => RegisterAsync(repository, account, previousPublicBaseUrl: null, cancellationToken);

    /// <summary>
    /// Creates or updates the repository's hook. <paramref name="previousPublicBaseUrl"/> is the Webhook
    /// Public Base URL before a change: a hook still pointing at it is re-pointed rather than duplicated.
    /// </summary>
    public async Task<WebhookRegistrationOutcome> RegisterAsync(
        MonitoredRepository repository,
        Credential account,
        string? previousPublicBaseUrl,
        CancellationToken cancellationToken)
    {
        WebhookIngressState ingress = await webhookIngress.GetStateAsync(cancellationToken);
        if (!ingress.IsConfigured)
        {
            return Skipped(WebhookRegistrationOutcome.NotConfiguredReason);
        }

        switch (account.BranchRulesToken)
        {
            case null:
                return Skipped(WebhookRegistrationOutcome.NoServerTokenReason);
            case ProviderToken.Unreadable:
                return Skipped(WebhookRegistrationOutcome.ServerTokenUnreadableReason);
        }

        if (WebhookProviders.From(account) is not { } provider)
        {
            return Failed(WebhookRegistrationOutcome.ProviderErrorReason, UnsupportedProviderDetail);
        }

        string serverToken = ((ProviderToken.Present)account.BranchRulesToken).Value;
        string payloadUrl = ingress.PayloadUrl(provider, repository.WebhookHookId)!;

        List<string> previousUrls = [];
        if (!string.IsNullOrEmpty(previousPublicBaseUrl)
            && new WebhookIngressState(true, previousPublicBaseUrl, ingress.BackstopInterval)
                .PayloadUrl(provider, repository.WebhookHookId) is { } previousUrl
            && !string.Equals(previousUrl, payloadUrl, StringComparison.Ordinal))
        {
            previousUrls.Add(previousUrl);
        }

        WebhookHookSpec spec = new(payloadUrl, repository.EnsureWebhookSecret(), previousUrls);

        Result<WebhookUpsertKind> result = await UpsertAsync(repository.Slug, account, serverToken, spec, cancellationToken);

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
        string serverToken,
        WebhookHookSpec spec,
        CancellationToken cancellationToken)
    {
        try
        {
            IIssueProvider provider = providerFactory.CreateProvider(account, serverToken);
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
