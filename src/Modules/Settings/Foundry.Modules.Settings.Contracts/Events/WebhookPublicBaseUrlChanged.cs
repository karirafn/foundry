using Foundry.Shared;

namespace Foundry.Modules.Settings.Contracts;

/// <summary>
/// Raised when the Webhook Public Base URL changes to a new, non-empty value. Monitoring re-points
/// every registered repository webhook at the new payload URL with the account's server-side token.
/// </summary>
/// <param name="PreviousPublicBaseUrl">The base URL before the change; <see langword="null"/> when unset.</param>
/// <param name="PublicBaseUrl">The new base URL.</param>
public sealed record WebhookPublicBaseUrlChanged(string? PreviousPublicBaseUrl, string PublicBaseUrl) : IIntegrationEvent;
