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
    private const char DriveSeparator = ':';
    private const string ParentSegment = "..";
    private const string CurrentSegment = ".";
    private const int DriveRootLength = 3;
    private const int DriveSeparatorIndex = 1;

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
        path.StartsWith(PosixSeparator) || IsWindowsDrivePath(path);

    internal static bool ContainsPathTraversal(string path) =>
        Array.Exists(path.Replace(WindowsSeparator, PosixSeparator).Split(PosixSeparator), s => s == ParentSegment);

    // Docker and the kernel resolve "//etc" and "/./etc" to "/etc", so every comparison runs on this form.
    internal static string Canonicalize(string posixPath)
    {
        string[] segments = posixPath.Split(
            PosixSeparator,
            StringSplitOptions.RemoveEmptyEntries);

        return PosixSeparator + string.Join(PosixSeparator, segments.Where(s => s != CurrentSegment));
    }

    internal static string CanonicalizeHostPath(string hostPath) =>
        IsWindowsDrivePath(hostPath) ? hostPath : Canonicalize(hostPath);

    internal static bool HasInvalidContainerPathCharacters(string containerPath) =>
        containerPath.Contains(DriveSeparator, StringComparison.Ordinal) || ContainsControlCharacter(containerPath);

    internal static bool HasInvalidHostPathCharacters(string hostPath)
    {
        string withoutDrive = IsWindowsDrivePath(hostPath)
            ? hostPath.Remove(DriveSeparatorIndex, 1)
            : hostPath;

        return withoutDrive.Contains(DriveSeparator, StringComparison.Ordinal) || ContainsControlCharacter(hostPath);
    }

    internal static bool IsSensitiveContainerPath(string containerPath)
    {
        string canonical = Canonicalize(containerPath);

        return canonical == RootPath || IsUnderSensitivePrefix(canonical);
    }

    internal static bool IsSensitiveHostPath(string hostPath)
    {
        if (IsWindowsDrivePath(hostPath))
        {
            return false;
        }

        string canonical = Canonicalize(hostPath);

        return canonical == RootPath
            || IsUnderSensitivePrefix(canonical)
            || IsAncestorOfSensitivePath(canonical);
    }

    private static bool IsWindowsDrivePath(string path) =>
        path.Length >= DriveRootLength
        && char.IsLetter(path[0])
        && path[DriveSeparatorIndex] == DriveSeparator
        && path[2] is WindowsSeparator or PosixSeparator;

    private static bool ContainsControlCharacter(string path) =>
        path.Any(char.IsControl);

    private static bool IsUnderSensitivePrefix(string canonical) =>
        Array.Exists(SensitivePrefixes, prefix => IsSameOrDescendant(canonical, prefix));

    private static bool IsAncestorOfSensitivePath(string canonical) =>
        Array.Exists(SensitivePrefixes, prefix => IsSameOrDescendant(prefix, canonical))
        || IsSameOrDescendant(DockerSocketPath, canonical);

    private static bool IsSameOrDescendant(string path, string ancestor) =>
        path == ancestor || path.StartsWith(ancestor + PosixSeparator, StringComparison.Ordinal);
}
