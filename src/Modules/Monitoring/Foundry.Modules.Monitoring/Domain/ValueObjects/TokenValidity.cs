namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// The provider's verdict on the account token. <see cref="Invalid"/> is an <em>Invalid Token</em>: the
/// provider refused the token with HTTP 401 on a runtime call, confirmed by a token check.
/// </summary>
public abstract record TokenValidity
{
    private TokenValidity() { }

    /// <summary>The provider has not refused the token.</summary>
    public sealed record Valid : TokenValidity;

    /// <summary>
    /// The provider refused the token. <paramref name="DetectedAt"/> is when the refusal was first
    /// confirmed; <paramref name="LastCheckedAt"/> is when the token was last checked and still refused.
    /// </summary>
    public sealed record Invalid(DateTimeOffset DetectedAt, DateTimeOffset LastCheckedAt) : TokenValidity;
}
