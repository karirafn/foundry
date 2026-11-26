using Foundry.Shared;

namespace Foundry.Modules.Credentials.Domain.Entities;

internal static class CredentialsErrors
{
    internal const string NotFoundCode = "Credentials.NotFound";
    internal const string InvalidAuthModeCode = "Credentials.InvalidAuthMode";
    internal const string LoginCodeEmptyCode = "Credentials.LoginCodeEmpty";
    internal const string LoginCodeTooLongCode = "Credentials.LoginCodeTooLong";
    internal const string LoginCodeHasControlCharactersCode = "Credentials.LoginCodeHasControlCharacters";
    internal const string LoginCodeIsWebAddressCode = "Credentials.LoginCodeIsWebAddress";
    internal const string LoginCodeIncompleteCode = "Credentials.LoginCodeIncomplete";

    internal static readonly Error NotFound =
        new(NotFoundCode, "Claude account credentials were not found.")
        {
            Kind = ErrorKind.NotFound,
        };

    internal static readonly Error InvalidAuthMode =
        new(InvalidAuthModeCode, "The specified authentication mode is not valid.");

    internal static readonly Error LoginCodeEmpty =
        new(LoginCodeEmptyCode, "Authorization code must not be empty.");

    internal static readonly Error LoginCodeHasControlCharacters =
        new(
            LoginCodeHasControlCharactersCode,
            "The code contains line breaks or control characters. Copy just the code and paste it again.");

    internal static readonly Error LoginCodeIsWebAddress =
        new(
            LoginCodeIsWebAddressCode,
            "That's a web address, not the code. Paste the code the last sign-in page shows.");

    internal static readonly Error LoginCodeIncomplete =
        new(
            LoginCodeIncompleteCode,
            "That doesn't look like the full code. Copy all of it, including the # in the middle.");

    internal static Error LoginCodeTooLong(int maxLength) =>
        new(LoginCodeTooLongCode, $"Authorization code must not exceed {maxLength} characters.");
}
