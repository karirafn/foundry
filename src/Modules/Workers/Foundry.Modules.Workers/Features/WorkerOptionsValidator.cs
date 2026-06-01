using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Workers:ApiKey must be non-empty. Set the Anthropic API key before starting.");
        }

        if (string.IsNullOrWhiteSpace(options.Image))
        {
            failures.Add("Workers:Image must be non-empty.");
        }
        else if (options.Image.EndsWith(":latest", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Workers:Image must not use the ':latest' tag. Pin to a specific version tag or digest for reproducible builds.");
        }

        if (string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            failures.Add("Workers:ConfigPath must be non-empty.");
        }
        else if (ContainsPathTraversal(options.ConfigPath))
        {
            failures.Add("Workers:ConfigPath must not contain path traversal segments (..).");
        }

        if (string.IsNullOrWhiteSpace(options.ReportsPath))
        {
            failures.Add("Workers:ReportsPath must be non-empty.");
        }
        else if (ContainsPathTraversal(options.ReportsPath))
        {
            failures.Add("Workers:ReportsPath must not contain path traversal segments (..).");
        }

        if (!options.WorkerPromptTemplate.Contains("{issueNumber}", StringComparison.Ordinal))
        {
            failures.Add("Workers:WorkerPromptTemplate must contain the {issueNumber} token.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static bool ContainsPathTraversal(string path)
    {
        string[] segments = path.Replace('\\', '/').Split('/');
        return Array.Exists(segments, s => s == "..");
    }
}
