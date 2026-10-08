using Foundry.Shared;

namespace Foundry.Modules.Credentials.Domain.Entities;

internal static class CredentialsErrors
{
    internal const string NotFoundCode = "Credentials.NotFound";
    internal const string InvalidAuthModeCode = "Credentials.InvalidAuthMode";
    internal const string LoginCodeEmptyCode = "Credentials.LoginCodeEmpty";
    internal const string LoginCodeTooLongCode = "Credentials.LoginCodeTooLong";

    internal static readonly Error NotFound =
        new(NotFoundCode, "Claude account credentials were not found.")
        {
            Kind = ErrorKind.NotFound,
        };

    internal static readonly Error InvalidAuthMode =
        new(InvalidAuthModeCode, "The specified authentication mode is not valid.");

    internal static readonly Error LoginCodeEmpty =
        new(LoginCodeEmptyCode, "Authorization code must not be empty.");

    internal static Error LoginCodeTooLong(int maxLength) =>
        new(LoginCodeTooLongCode, $"Authorization code must not exceed {maxLength} characters.");
}
