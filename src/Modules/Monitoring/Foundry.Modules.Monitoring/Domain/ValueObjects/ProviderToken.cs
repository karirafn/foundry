namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// Models the state of a provider token stored in the credential.
/// A null token column models absence; <see cref="Unreadable"/> models a token that
/// is present in the database but could not be decrypted (e.g. CryptographicException
/// or FormatException from data-protection).
/// </summary>
public abstract record ProviderToken
{
    private ProviderToken() { }

    /// <summary>A token that was successfully decrypted and is ready to use.</summary>
    public sealed record Present(string Value) : ProviderToken;

    /// <summary>
    /// A token that exists in the database but cannot be decrypted —
    /// the stored ciphertext is invalid or the key is unavailable.
    /// </summary>
    public sealed record Unreadable : ProviderToken;
}
