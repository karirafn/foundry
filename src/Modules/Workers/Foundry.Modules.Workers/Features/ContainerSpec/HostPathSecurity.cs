namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal static class HostPathSecurity
{
    private const string DockerSocketPath = "/var/run/docker.sock";

    private static readonly string[] SensitiveHostPrefixes =
    [
        "/etc",
        "/proc",
        "/sys",
        "/dev",
        "/run",
        "/var/run",
    ];

    internal static bool IsSensitiveHostPath(string hostPath)
    {
        string normalized = hostPath.TrimEnd('/');

        if (normalized == DockerSocketPath)
        {
            return true;
        }

        return Array.Exists(
            SensitiveHostPrefixes,
            prefix => prefix == normalized
                || normalized.StartsWith(prefix + "/", StringComparison.Ordinal));
    }
}
