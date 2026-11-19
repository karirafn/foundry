namespace Foundry.Modules.Monitoring.Features.CredentialResolution;

/// <summary>
/// Shared log-message templates for credential warning diagnostics emitted at the application layer.
/// </summary>
internal static class CredentialWarnings
{
    /// <summary>
    /// Warning logged when a credential is loaded but its stored ciphertext could not be decrypted.
    /// Structured-logging placeholder: <c>{CredentialId}</c>.
    /// </summary>
    internal const string UnreadableToken =
        "Credential {CredentialId} loaded with an unreadable accounts.token — " +
        "the stored ciphertext could not be decrypted. The credential is treated as ineligible until the token is re-entered.";
}
