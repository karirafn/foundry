namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal abstract class WritePermissionProbeResult
{
    private WritePermissionProbeResult() { }

    internal sealed class Granted : WritePermissionProbeResult;

    internal sealed class Missing(WritePermission permission) : WritePermissionProbeResult
    {
        public WritePermission Permission { get; } = permission;
    }
}
