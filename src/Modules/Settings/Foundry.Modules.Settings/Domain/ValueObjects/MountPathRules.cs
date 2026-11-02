namespace Foundry.Modules.Settings.Domain.ValueObjects;

/// <summary>
/// Path rules for worker bind mounts. The sensitive-path lists intentionally mirror the Workers module's
/// container-spec rules: Settings cannot reference Workers internals across the assembly boundary.
/// </summary>
internal static class MountPathRules
{
    private const string RootPath = "/";
    private const string DockerSocketPath = "/var/run/docker.sock";
    private const char PosixSeparator = '/';
    private const char WindowsSeparator = '\\';
    private const string ParentSegment = "..";
    private const int DriveRootLength = 3;

    private static readonly string[] SensitivePrefixes =
    [
        "/etc",
        "/proc",
        "/sys",
        "/dev",
        "/run",
        "/var/run",
    ];

    // A Windows host path such as C:\dev\skills is absolute even though Path.IsPathRooted
    // reports false for it on Linux, so both root forms are checked explicitly.
    internal static bool IsAbsolutePath(string path) =>
        path.StartsWith(PosixSeparator)
        || (path.Length >= DriveRootLength
            && char.IsLetter(path[0])
            && path[1] == ':'
            && path[2] is WindowsSeparator or PosixSeparator);

    internal static bool ContainsPathTraversal(string path) =>
        Array.Exists(path.Replace(WindowsSeparator, PosixSeparator).Split(PosixSeparator), s => s == ParentSegment);

    internal static bool IsSensitiveContainerPath(string containerPath)
    {
        string trimmed = containerPath.TrimEnd(PosixSeparator);
        string normalized = trimmed.Length == 0 ? RootPath : trimmed;

        return normalized == RootPath || IsSensitivePrefixMatch(normalized);
    }

    internal static bool IsSensitiveHostPath(string hostPath)
    {
        string normalized = hostPath.TrimEnd(PosixSeparator);

        return normalized == DockerSocketPath || IsSensitivePrefixMatch(normalized);
    }

    private static bool IsSensitivePrefixMatch(string normalized) =>
        Array.Exists(
            SensitivePrefixes,
            prefix => prefix == normalized || normalized.StartsWith(prefix + PosixSeparator, StringComparison.Ordinal));
}
