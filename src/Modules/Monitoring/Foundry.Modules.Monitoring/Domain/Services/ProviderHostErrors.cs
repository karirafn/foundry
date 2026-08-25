using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Services;

internal static class ProviderHostErrors
{
    public static Error NotAllowed(string host) => new(
        "ProviderHost.NotAllowed",
        $"Host '{host}' is not in the accepted provider hosts list. " +
        "Add it to the allowed hosts in Global Settings if it is a legitimate self-hosted provider.");

    public static Error ResolvesToPrivateAddress(string host) => new(
        "ProviderHost.ResolvesToPrivateAddress",
        $"Host '{host}' resolves to a private, loopback, or link-local IP address. " +
        "Provider base URLs must resolve to publicly-routable addresses only.");
}
