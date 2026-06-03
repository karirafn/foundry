using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
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

        bool hasApiKey = !string.IsNullOrWhiteSpace(options.ApiKey);
        bool hasOAuthToken = !string.IsNullOrWhiteSpace(options.OAuthToken);

        if (hasApiKey && hasOAuthToken)
        {
            failures.Add("Set exactly one of Workers:ApiKey or Workers:OAuthToken, not both.");
        }
        else if (!hasApiKey && !hasOAuthToken)
        {
            failures.Add("Set exactly one of Workers:ApiKey (pay-per-use) or Workers:OAuthToken (Max plan via claude setup-token).");
        }

        if (string.IsNullOrWhiteSpace(options.Image))
        {
            failures.Add("Workers:Image must be non-empty.");
        }
        else if (options.Image.EndsWith(":latest", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Workers:Image must not use the ':latest' tag. Pin to a specific version tag or digest for reproducible builds.");
        }

        if (string.IsNullOrWhiteSpace(options.ReportsPath))
        {
            failures.Add("Workers:ReportsPath must be non-empty.");
        }
        else if (ContainsPathTraversal(options.ReportsPath))
        {
            failures.Add("Workers:ReportsPath must not contain path traversal segments (..).");
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

    private static bool ContainsPathTraversal(string path)
    {
        string[] segments = path.Replace('\\', '/').Split('/');
        return Array.Exists(segments, s => s == "..");
    }
}
