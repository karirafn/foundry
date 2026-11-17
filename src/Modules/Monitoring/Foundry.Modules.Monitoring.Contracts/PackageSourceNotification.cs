namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The system notification that tells open Settings pages a Package Source's status may have changed, so they read
/// the list again. It carries no message and never shows a banner.
/// </summary>
public static class PackageSourceNotification
{
    public const string Category = "package-source";
}
