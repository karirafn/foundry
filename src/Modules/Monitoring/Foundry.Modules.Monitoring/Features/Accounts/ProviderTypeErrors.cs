using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class ProviderTypeErrors
{
    internal const string UnknownProviderTypeCode = "ProviderType.Unknown";

    private const int ProviderTypeDisplayMaxLength = 64;

    internal static Error UnknownProviderType(string providerType)
    {
        string displayValue = providerType.Length > ProviderTypeDisplayMaxLength
            ? providerType[..ProviderTypeDisplayMaxLength]
            : providerType;
        return new Error(
            UnknownProviderTypeCode,
            $"Provider type '{displayValue}' is not supported. Only 'github' and 'gitlab' are supported.");
    }
}
