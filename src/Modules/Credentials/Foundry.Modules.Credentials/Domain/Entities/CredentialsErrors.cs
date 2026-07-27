using Foundry.Shared;

namespace Foundry.Modules.Credentials.Domain.Entities;

internal static class CredentialsErrors
{
    internal const string NotFoundCode = "Credentials.NotFound";
    internal const string InvalidAuthModeCode = "Credentials.InvalidAuthMode";

    internal static readonly Error NotFound =
        new(NotFoundCode, "Claude account credentials were not found.")
        {
            Kind = ErrorKind.NotFound,
        };

    internal static readonly Error InvalidAuthMode =
        new(InvalidAuthModeCode, "The specified authentication mode is not valid.");
}
