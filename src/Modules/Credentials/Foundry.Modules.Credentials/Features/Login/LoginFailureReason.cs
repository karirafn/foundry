namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Typed reason hierarchy for a failed OAuth login session.
/// Each variant maps to distinct UI copy in <c>OAuthPanelComponent</c>.
/// Not persisted — login sessions are transient in-memory state.
/// </summary>
internal abstract record LoginFailureReason
{
    private protected LoginFailureReason() { }

    /// <summary>The code supplied by the operator was wrong or had already expired.</summary>
    internal sealed record InvalidCode(string? Message = null) : LoginFailureReason;

    /// <summary>No authorization URL appeared in the container log within the configured timeout.</summary>
    internal sealed record UrlTimeout(string? Message = null) : LoginFailureReason;

    /// <summary>A URL was received but no code was submitted within the configured session timeout.</summary>
    internal sealed record CodeTimeout(string? Message = null) : LoginFailureReason;

    /// <summary>Any other error — container non-zero exit, exec failure, identity parse failure, etc.</summary>
    internal sealed record Unknown(string? Message = null) : LoginFailureReason;
}
