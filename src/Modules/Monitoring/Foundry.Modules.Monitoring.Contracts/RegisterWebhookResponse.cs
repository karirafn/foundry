namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The 200 body of <c>POST /api/accounts/{accountId}/repositories/{id}/webhook/register</c>: the
/// registration details after the attempt and the attempt's outcome.
/// </summary>
public sealed record RegisterWebhookResponse(
    RepositoryWebhookInfo Webhook,
    WebhookRegistrationOutcome Outcome);
