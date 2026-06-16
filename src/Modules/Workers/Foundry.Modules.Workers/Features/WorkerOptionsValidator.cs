using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Features.ImageBuild;

using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features;

internal sealed partial class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    [GeneratedRegex(@"^[A-Z_][A-Z0-9_]*$")]
    private static partial Regex BuildArgKeyPattern();

    [GeneratedRegex(@"^(Bash|Edit|Read|Write|MultiEdit|Glob|Grep)\(.+\)$")]
    private static partial Regex DenyRuleFormatPattern();

    private static readonly string[] SensitiveContainerPrefixes =
    [
        "/",
        "/etc",
        "/proc",
        "/sys",
        "/dev",
        "/run",
        "/var/run",
    ];

    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.Image))
        {
            failures.Add("Workers:Image must be non-empty.");
        }
        else if (options.Image.EndsWith(":latest", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Workers:Image must not use the ':latest' tag. Pin to a specific version tag or digest for reproducible builds.");
        }

        ValidateMountDictionary(options.Mounts, "Workers:Mounts", failures);
        ValidateMountDictionary(options.WritableMounts, "Workers:WritableMounts", failures);

        HashSet<string> writableNormalized = options.WritableMounts.Keys
            .Select(k => k.TrimEnd('/'))
            .ToHashSet(StringComparer.Ordinal);

        foreach (string containerPath in options.Mounts.Keys)
        {
            if (writableNormalized.Contains(containerPath.TrimEnd('/')))
            {
                failures.Add(
                    $"Container path '{containerPath}' appears in both Workers:Mounts and Workers:WritableMounts. Each container path must appear in at most one mount dictionary.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.WorkerPromptTemplate))
        {
            failures.Add("Workers:WorkerPromptTemplate must be non-empty.");
        }
        else if (!options.WorkerPromptTemplate.Contains("{issueNumber}", StringComparison.Ordinal))
        {
            failures.Add("Workers:WorkerPromptTemplate must contain the {issueNumber} token.");
        }

        if (options.BranchNamingInstruction.Contains('\n', StringComparison.Ordinal)
            || options.BranchNamingInstruction.Contains('\r', StringComparison.Ordinal)
            || options.BranchNamingInstruction.Contains('\0', StringComparison.Ordinal))
        {
            failures.Add("Workers:BranchNamingInstruction must not contain newline or null characters.");
        }

        ValidateSettings(options.Settings, failures);
        ValidateImageBuild(options.ImageBuild, failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateMountDictionary(
        IReadOnlyDictionary<string, string> mounts,
        string configKey,
        List<string> failures)
    {
        foreach (KeyValuePair<string, string> mount in mounts)
        {
            string containerPath = mount.Key;
            string hostPath = mount.Value;

            if (!containerPath.StartsWith('/'))
            {
                failures.Add(
                    $"{configKey} container path '{containerPath}' must be absolute (start with '/').");
            }
            else if (ContainsPathTraversal(containerPath))
            {
                failures.Add(
                    $"{configKey} container path '{containerPath}' must not contain path traversal segments (..).");
            }
            else if (IsSensitiveContainerPrefix(containerPath))
            {
                failures.Add(
                    $"{configKey} container path '{containerPath}' targets a sensitive system directory and is not allowed.");
            }

            if (!IsAbsolutePath(hostPath))
            {
                failures.Add(
                    $"{configKey} host path '{hostPath}' must be absolute.");
            }
            else if (ContainsPathTraversal(hostPath))
            {
                failures.Add(
                    $"{configKey} host path '{hostPath}' must not contain path traversal segments (..).");
            }
            else if (HostPathSecurity.IsSensitiveHostPath(hostPath))
            {
                failures.Add(
                    $"{configKey} host path '{hostPath}' targets a sensitive system directory and is not allowed.");
            }
        }
    }

    private static bool IsSensitiveContainerPrefix(string containerPath)
    {
        string trimmed = containerPath.TrimEnd('/');
        string normalized = trimmed.Length == 0 ? "/" : trimmed;
        return Array.Exists(
            SensitiveContainerPrefixes,
            prefix => prefix == normalized
                || (prefix != "/" && normalized.StartsWith(prefix + "/", StringComparison.Ordinal)));
    }

    private static bool IsAbsolutePath(string path)
    {
        return path.StartsWith('/')
            || (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'));
    }

    private static void ValidateImageBuild(ImageBuildOptions imageBuild, List<string> failures)
    {
        if (!imageBuild.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(imageBuild.ContextPath))
        {
            failures.Add("Workers:ImageBuild:ContextPath must be non-empty when ImageBuild is enabled.");
        }
        else if (IsAbsolutePath(imageBuild.ContextPath))
        {
            failures.Add(
                "Workers:ImageBuild:ContextPath must be a relative path. " +
                "It is resolved against the solution root at startup.");
        }
        else if (ContainsPathTraversal(imageBuild.ContextPath))
        {
            failures.Add("Workers:ImageBuild:ContextPath must not contain path traversal segments (..).");
        }

        foreach (KeyValuePair<string, string> buildArg in imageBuild.BuildArgs)
        {
            if (!BuildArgKeyPattern().IsMatch(buildArg.Key))
            {
                failures.Add(
                    $"Workers:ImageBuild:BuildArgs key '{buildArg.Key}' is invalid. " +
                    "Keys must match ^[A-Z_][A-Z0-9_]*$ (Docker build arg convention).");
            }

            if (buildArg.Value.Contains('\n', StringComparison.Ordinal)
                || buildArg.Value.Contains('\r', StringComparison.Ordinal)
                || buildArg.Value.Contains('\0', StringComparison.Ordinal))
            {
                failures.Add(
                    $"Workers:ImageBuild:BuildArgs value for key '{buildArg.Key}' must not contain newlines, carriage returns, or null bytes.");
            }
        }
    }

    private static bool ContainsPathTraversal(string path)
    {
        string[] segments = path.Replace('\\', '/').Split('/');
        return Array.Exists(segments, s => s == "..");
    }

    private static void ValidateSettings(WorkerSettingsOptions settings, List<string> failures)
    {
        foreach (string rule in settings.CiCdDenyRules)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                failures.Add("Workers:Settings:CiCdDenyRules entries must be non-empty.");
            }
            else if (!DenyRuleFormatPattern().IsMatch(rule))
            {
                failures.Add(
                    "Workers:Settings:CiCdDenyRules entries must follow the format Tool(pattern:*) " +
                    "where Tool is one of Bash, Edit, Read, Write, MultiEdit, Glob, Grep.");
            }
        }

        foreach (string rule in settings.AdditionalDenyRules)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                failures.Add("Workers:Settings:AdditionalDenyRules entries must be non-empty.");
            }
            else if (!DenyRuleFormatPattern().IsMatch(rule))
            {
                failures.Add(
                    "Workers:Settings:AdditionalDenyRules entries must follow the format Tool(pattern:*) " +
                    "where Tool is one of Bash, Edit, Read, Write, MultiEdit, Glob, Grep.");
            }
        }

        foreach (KeyValuePair<string, List<HookGroup>> hookEvent in settings.Hooks)
        {
            foreach (HookGroup group in hookEvent.Value)
            {
                foreach (HookEntry entry in group.Hooks)
                {
                    if (entry.Type != "command")
                    {
                        failures.Add(
                            $"Workers:Settings:Hooks hook type '{entry.Type}' is not supported. Only \"command\" is supported.");
                    }

                    if (string.IsNullOrWhiteSpace(entry.Command))
                    {
                        failures.Add("Workers:Settings:Hooks hook Command must be non-empty.");
                    }
                }
            }
        }
    }
}
