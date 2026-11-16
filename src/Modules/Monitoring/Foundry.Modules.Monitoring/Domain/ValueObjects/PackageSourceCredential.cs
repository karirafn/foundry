namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// A username and token the operator registered for one Package Source. Foundry reads the source with it for every
/// repository that declares the source, instead of the account credential (ADR 0112). The token is encrypted at rest
/// and never leaves the server.
/// </summary>
internal sealed record PackageSourceCredential
{
    internal const int MaxUsernameLength = 255;
    internal const int MaxTokenLength = 2048;

    internal PackageSourceCredential(string username, ProviderToken token, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (UsernameError(username) is { } usernameError)
        {
            throw new ArgumentException(usernameError, nameof(username));
        }

        if (token is ProviderToken.Present present && TokenError(present.Value) is { } tokenError)
        {
            throw new ArgumentException(tokenError, nameof(token));
        }

        Username = username;
        Token = token;
        UpdatedAt = updatedAt;
    }

    public string Username { get; }

    /// <summary>The token; <see cref="ProviderToken.Unreadable"/> when the stored value can no longer be decrypted.</summary>
    public ProviderToken Token { get; }

    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// The rules a username and token must meet: both are required (empty is missing; whitespace is kept as given),
    /// and at most <see cref="MaxUsernameLength"/> and <see cref="MaxTokenLength"/> characters. Empty when both hold.
    /// </summary>
    public static IReadOnlyList<PackageSourceCredentialFieldError> Validate(string? username, string? token)
    {
        List<PackageSourceCredentialFieldError> errors = [];

        if (UsernameError(username) is { } usernameError)
        {
            errors.Add(new PackageSourceCredentialFieldError(PackageSourceCredentialField.Username, usernameError));
        }

        if (TokenError(token) is { } tokenError)
        {
            errors.Add(new PackageSourceCredentialFieldError(PackageSourceCredentialField.Token, tokenError));
        }

        return errors;
    }

    // The token never reaches a log or an exception message.
    public override string ToString() => $"{Username}:***";

    private static string? UsernameError(string? username) => username switch
    {
        null or "" => "Username is required.",
        { Length: > MaxUsernameLength } => $"Username must be {MaxUsernameLength} characters or fewer.",
        _ => null,
    };

    private static string? TokenError(string? token) => token switch
    {
        null or "" => "Token is required.",
        { Length: > MaxTokenLength } => $"Token must be {MaxTokenLength} characters or fewer.",
        _ => null,
    };
}

internal enum PackageSourceCredentialField
{
    Username,
    Token,
}

/// <summary>Why one <see cref="PackageSourceCredential"/> field was refused.</summary>
internal sealed record PackageSourceCredentialFieldError(PackageSourceCredentialField Field, string Message);
