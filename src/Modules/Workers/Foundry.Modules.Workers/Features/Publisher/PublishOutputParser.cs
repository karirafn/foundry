using System.Globalization;
using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>
/// Reads a Publish run's exit: its single structured <c>FOUNDRY_PUBLISH</c> result line, and the
/// <c>FOUNDRY_RUN_COMMIT</c> lines naming the commits it pushed, and the Diff gate's <c>FOUNDRY_REVERTED_FILE</c> and
/// <c>FOUNDRY_SECRET_FINDING</c> lines. Anything else the Publisher printed is diagnostics.
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
            "diverged" => ReadDiverged(values, log),
            "secret_detected" => ReadSecretDetected(values, log),
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
        return new PublishResult.Published(head, commits, subjects, ReadRevertedFiles(log));
    }

    private static PublishResult.Diverged? ReadDiverged(Dictionary<string, string> values, string log)
    {
        if (!TryBranch(values.GetValueOrDefault("branch"), out BranchName branch)
            || !TryBranch(values.GetValueOrDefault("side_branch"), out BranchName sideBranch)
            || !CommitSha.TryFrom(values.GetValueOrDefault("head"), out CommitSha head)
            || ReadDivergenceReason(values.GetValueOrDefault("reason")) is not { } reason)
        {
            return null;
        }

        return new PublishResult.Diverged(branch, sideBranch, head, reason, ReadRevertedFiles(log));
    }

    // No reason is a branch that moved during the run; the Publisher names only a refused force push (ADR 0101).
    private static BranchDivergenceReason? ReadDivergenceReason(string? reason) => reason is null
        ? BranchDivergenceReason.BranchMoved
        : BranchDivergenceReasonTokens.FromToken(reason.Trim());

    // Nothing was pushed, so a finding count that disagrees with the finding lines is never taken as a result.
    private static PublishResult.SecretDetected? ReadSecretDetected(Dictionary<string, string> values, string log)
    {
        if (!int.TryParse(values.GetValueOrDefault("findings"), NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            || count == 0)
        {
            return null;
        }

        List<SecretFinding> findings = [];
        foreach (Match match in SecretFindingLinePattern().Matches(log))
        {
            if (!CommitSha.TryFrom(match.Groups["commit"].Value, out CommitSha commit)
                || !int.TryParse(match.Groups["line"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int line)
                || line < 1)
            {
                return null;
            }

            findings.Add(new SecretFinding(match.Groups["path"].Value, line, commit, match.Groups["rule"].Value));
        }

        return findings.Count == count ? new PublishResult.SecretDetected(findings) : null;
    }

    // The files are already pushed, so an unreadable line is skipped rather than failing the publish.
    private static List<RevertedFile> ReadRevertedFiles(string log)
    {
        List<RevertedFile> files = [];
        foreach (Match match in RevertedFileLinePattern().Matches(log))
        {
            RevertedFileReason? reason = match.Groups["reason"].Value switch
            {
                "binary" => RevertedFileReason.Binary,
                "oversized" => RevertedFileReason.Oversized,
                _ => null,
            };

            if (reason is { } known
                && long.TryParse(match.Groups["size"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long size))
            {
                files.Add(new RevertedFile(match.Groups["path"].Value, known, size));
            }
        }

        return files;
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

    // The path is the last field and may hold spaces; the Publisher rejects a path holding a line break.
    [GeneratedRegex(
        @"^(?:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) )?FOUNDRY_SECRET_FINDING commit=(?<commit>[0-9a-f]{40,64}) line=(?<line>[0-9]{1,9}) rule=(?<rule>[A-Za-z0-9._-]{1,200}) path=(?<path>[^\r\n]+?)\r?$",
        RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretFindingLinePattern();

    [GeneratedRegex(
        @"^(?:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) )?FOUNDRY_REVERTED_FILE reason=(?<reason>[a-z]+) size=(?<size>[0-9]{1,18}) path=(?<path>[^\r\n]+?)\r?$",
        RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex RevertedFileLinePattern();
}
