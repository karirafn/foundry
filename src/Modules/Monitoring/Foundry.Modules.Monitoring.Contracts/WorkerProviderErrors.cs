using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public static class WorkerProviderErrors
{
    private const string UnknownDiscriminatorCode = "WorkerProvider.UnknownDiscriminator";

    public static Error UnknownDiscriminator(string discriminator) =>
        new(UnknownDiscriminatorCode, $"Unknown provider discriminator: '{discriminator}'.");
}
