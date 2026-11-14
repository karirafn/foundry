namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// The account's decision on whether its token can be used for a provider call. Every consumer
/// switches on this closed set, so a new outcome cannot be silently treated as usable.
/// </summary>
public abstract record TokenUsability
{
    private TokenUsability() { }

    /// <summary>The token was decrypted and may be sent to the provider.</summary>
    public sealed record Usable(string Token) : TokenUsability;

    /// <summary>A token is stored but Foundry cannot decrypt it.</summary>
    public sealed record Unreadable : TokenUsability;

    /// <summary>No token is configured on the account.</summary>
    public sealed record Absent : TokenUsability;
}
