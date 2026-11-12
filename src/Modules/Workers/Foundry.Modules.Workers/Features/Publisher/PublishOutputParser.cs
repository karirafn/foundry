using System.Globalization;
using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>
/// Reads a Publish run's exit: its single structured <c>FOUNDRY_PUBLISH</c> result line, and the
/// <c>FOUNDRY_RUN_COMMIT</c> lines naming the commits it pushed. Anything else the Publisher printed is diagnostics.
/// </summary>
internal static partial class PublishOutputParser
{
    public const string MissingResultMessage = "The Publisher finished without reporting a publish result.";

    private const int MaxReasonLength = 500;

    public static PublishResult Parse(long exitCode, string? log)
    {
        string text = log ?? string.Empty;
        MatchCollection results = ResultLinePattern().Matches(text);

        if (results.Count != 1 || ReadResult(results[0].Groups["fields"].Value, text) is not { } result)
        {
            return exitCode == 0
                ? new PublishResult.Failed(MissingResultMessage, Transient: false)
                : new PublishResult.Failed(
                    string.Create(CultureInfo.InvariantCulture, $"The Publisher exited with code {exitCode} without a publish result."),
                    Transient: true);
        }

        return result;
    }

    private static PublishResult? ReadResult(string fields, string log)
    {
        Dictionary<string, string> values = ReadFields(fields);

        return values.GetValueOrDefault("result") switch
        {
            "published" => ReadPublished(values, log),
            "nothing_to_publish" => new PublishResult.NothingToPublish(),
            "diverged" => ReadDiverged(values),
            "failed" => ReadFailed(values),
            _ => null,
        };
    }

    private static PublishResult.Published? ReadPublished(Dictionary<string, string> values, string log)
    {
        if (!CommitSha.TryFrom(values.GetValueOrDefault("head"), out CommitSha head)
            || !int.TryParse(values.GetValueOrDefault("commits"), NumberStyles.None, CultureInfo.InvariantCulture, out int commits))
        {
            return null;
        }

        List<string> subjects = [.. CommitLinePattern().Matches(log).Select(match => match.Groups["subject"].Value.Trim())];
        return new PublishResult.Published(head, commits, subjects);
    }

    private static PublishResult.Diverged? ReadDiverged(Dictionary<string, string> values)
    {
        if (!TryBranch(values.GetValueOrDefault("branch"), out BranchName branch)
            || !TryBranch(values.GetValueOrDefault("side_branch"), out BranchName sideBranch)
            || !CommitSha.TryFrom(values.GetValueOrDefault("head"), out CommitSha head))
        {
            return null;
        }

        return new PublishResult.Diverged(branch, sideBranch, head);
    }

    private static PublishResult.Failed? ReadFailed(Dictionary<string, string> values)
    {
        bool? transient = values.GetValueOrDefault("kind") switch
        {
            "transient" => true,
            "permanent" => false,
            _ => null,
        };

        if (transient is null)
        {
            return null;
        }

        string reason = SecretRedactor.Redact(values.GetValueOrDefault("reason") ?? string.Empty).Trim();
        if (reason.Length == 0)
        {
            reason = "The Publisher reported no reason.";
        }
        else if (reason.Length > MaxReasonLength)
        {
            reason = reason[..MaxReasonLength];
        }

        return new PublishResult.Failed(reason, transient.Value);
    }

    // The reason is the last field and may hold spaces, so it takes the rest of the line.
    private static Dictionary<string, string> ReadFields(string fields)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        int reasonAt = fields.IndexOf("reason=", StringComparison.Ordinal);
        string head = reasonAt >= 0 ? fields[..reasonAt] : fields;

        foreach (string field in head.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = field.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                values.TryAdd(field[..separator], field[(separator + 1)..]);
            }
        }

        if (reasonAt >= 0)
        {
            values["reason"] = fields[(reasonAt + "reason=".Length)..];
        }

        return values;
    }

    private static bool TryBranch(string? value, out BranchName branch)
    {
        if (value is not null && BranchPattern().IsMatch(value))
        {
            branch = BranchName.From(value);
            return true;
        }

        branch = default;
        return false;
    }

    // The branch-name characters the Publisher itself accepts.
    [GeneratedRegex("^[a-zA-Z0-9_/.-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BranchPattern();

    // Anchored to the start of a line (after the optional Docker timestamp), like the FOUNDRY_BASE line.
    [GeneratedRegex(
        @"^(?:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) )?FOUNDRY_PUBLISH (?<fields>[^\r\n]*?)\r?$",
        RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ResultLinePattern();

    [GeneratedRegex(
        @"^(?:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) )?FOUNDRY_RUN_COMMIT [0-9a-f]{40,64} (?<subject>[^\r\n]*?)\r?$",
        RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CommitLinePattern();
}
