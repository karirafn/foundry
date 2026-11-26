using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Credentials.Domain.ValueObjects;

/// <summary>
/// The authorization code the operator pastes from the last sign-in page, in the
/// <c>&lt;code&gt;#&lt;state&gt;</c> form the Claude CLI expects. The rule is deliberately no
/// stricter than the CLI's own (ADR 0024): the character set of either half is not checked.
/// </summary>
public sealed record LoginCode
{
    internal const int MaxLength = 512;

    public string Value { get; }

    private LoginCode(string value) => Value = value;

    public static Result<LoginCode> Create(string value)
    {
        string trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            return Result<LoginCode>.Fail(CredentialsErrors.LoginCodeEmpty);
        }

        if (trimmed.Length > MaxLength)
        {
            return Result<LoginCode>.Fail(CredentialsErrors.LoginCodeTooLong(MaxLength));
        }

        if (trimmed.Any(char.IsControl))
        {
            return Result<LoginCode>.Fail(CredentialsErrors.LoginCodeHasControlCharacters);
        }

        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return Result<LoginCode>.Fail(CredentialsErrors.LoginCodeIsWebAddress);
        }

        int hashIndex = trimmed.IndexOf('#', StringComparison.Ordinal);
        if (hashIndex <= 0 || hashIndex == trimmed.Length - 1)
        {
            return Result<LoginCode>.Fail(CredentialsErrors.LoginCodeIncomplete);
        }

        return new LoginCode(trimmed);
    }

    public override string ToString() => Value;
}
