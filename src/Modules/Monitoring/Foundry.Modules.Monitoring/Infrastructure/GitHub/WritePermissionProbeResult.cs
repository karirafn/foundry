namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal abstract class WritePermissionProbeResult
{
    private WritePermissionProbeResult() { }

    internal sealed class Granted : WritePermissionProbeResult;

    /// <summary>The token lacks every permission in <see cref="Permissions"/>, in probe order.</summary>
    internal sealed class Missing(IReadOnlyList<WritePermission> permissions) : WritePermissionProbeResult
    {
        public IReadOnlyList<WritePermission> Permissions { get; } = permissions;
    }
}
