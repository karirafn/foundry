using System.Text.Json.Serialization;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

/// <summary>
/// Where worker hooks come from: the operator's host Claude Code <c>settings.json</c>, bind-mounted
/// read-only into each worker and merged by the entrypoint, minus the hook commands in the exclusion list.
/// An empty <see cref="HostSettingsPath"/> means no host hooks are imported.
/// </summary>
public sealed record HostHooks
{
    internal const int MaxHostSettingsPathLength = 1024;
    internal const int MaxExcludedCommandLength = 4096;
    internal const int MaxExcludedCommandCount = 100;

    [JsonConstructor]
    private HostHooks(string hostSettingsPath, IReadOnlyList<string>? excludedCommands = null)
    {
        HostSettingsPath = hostSettingsPath;
        ExcludedCommands = excludedCommands ?? [];
    }

    public static readonly HostHooks Default = new(string.Empty, []);

    public string HostSettingsPath { get; }

    public IReadOnlyList<string> ExcludedCommands { get; }

    public static Result<HostHooks> Create(string hostSettingsPath, IReadOnlyList<string> excludedCommands)
    {
        string trimmedPath = hostSettingsPath.Trim();

        if (trimmedPath.Length > 0)
        {
            Result<string> pathResult = ValidatePath(trimmedPath);

            if (pathResult is not Result<string>.Success pathSuccess)
            {
                return Result<HostHooks>.Fail(((Result<string>.Failure)pathResult).Error);
            }

            trimmedPath = pathSuccess.Value;
        }

        if (excludedCommands.Count > MaxExcludedCommandCount)
        {
            return Result<HostHooks>.Fail(SettingsErrors.TooManyExcludedHookCommands(excludedCommands.Count));
        }

        HashSet<string> seenCommands = new(StringComparer.Ordinal);
        List<string> canonicalCommands = [];

        foreach (string command in excludedCommands)
        {
            string trimmedCommand = command.Trim();

            if (trimmedCommand.Length == 0)
            {
                return Result<HostHooks>.Fail(SettingsErrors.EmptyExcludedHookCommand);
            }

            if (trimmedCommand.Length > MaxExcludedCommandLength)
            {
                return Result<HostHooks>.Fail(SettingsErrors.ExcludedHookCommandTooLong);
            }

            if (seenCommands.Add(trimmedCommand))
            {
                canonicalCommands.Add(trimmedCommand);
            }
        }

        return Result<HostHooks>.Ok(new HostHooks(trimmedPath, canonicalCommands.AsReadOnly()));
    }

    public bool Equals(HostHooks? other) =>
        other is not null
        && HostSettingsPath == other.HostSettingsPath
        && ExcludedCommands.SequenceEqual(other.ExcludedCommands);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(HostSettingsPath);
        foreach (string command in ExcludedCommands)
        {
            hash.Add(command);
        }

        return hash.ToHashCode();
    }

    private static Result<string> ValidatePath(string path)
    {
        if (path.Length > MaxHostSettingsPathLength)
        {
            return Result<string>.Fail(SettingsErrors.HostSettingsPathTooLong);
        }

        if (!MountPathRules.IsAbsolutePath(path))
        {
            return Result<string>.Fail(SettingsErrors.HostSettingsPathNotAbsolute(path));
        }

        if (MountPathRules.ContainsPathTraversal(path))
        {
            return Result<string>.Fail(SettingsErrors.HostSettingsPathTraversal(path));
        }

        if (MountPathRules.HasInvalidHostPathCharacters(path))
        {
            return Result<string>.Fail(SettingsErrors.InvalidHostSettingsPathCharacters(path));
        }

        if (MountPathRules.IsSensitiveHostPath(path))
        {
            return Result<string>.Fail(SettingsErrors.SensitiveHostSettingsPath(path));
        }

        return Result<string>.Ok(MountPathRules.CanonicalizeHostPath(path));
    }
}
