using Foundry.Shared;

namespace Foundry.Modules.Credentials.Domain;

internal static class CredentialsErrors
{
    internal const string NotFoundCode = "Credentials.NotFound";

    internal static readonly Error NotFound =
        new(NotFoundCode, "Claude account credentials were not found.")
        {
            Kind = ErrorKind.NotFound,
        };
}
