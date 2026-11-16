using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// The provider identity an account's token authenticates as (<c>login</c> on GitHub,
/// <c>username</c> on GitLab).
/// </summary>
public sealed record AccountName
{
    internal const int MaxLength = 200;

    public string Value { get; }

    private AccountName(string value) => Value = value;

    public static Result<AccountName> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || value.Any(char.IsControl))
        {
            return Result<AccountName>.Fail(CredentialErrors.UnresolvedIdentity);
        }

        return new AccountName(value);
    }

    /// <summary>
    /// Rehydrates a stored name without validating it, so a legacy name entered by hand before names
    /// were derived from the token still loads and displays.
    /// </summary>
    public static AccountName FromPersistedString(string value) => new(value);

    public override string ToString() => Value;
}
