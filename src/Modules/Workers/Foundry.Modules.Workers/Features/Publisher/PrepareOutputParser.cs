using System.Globalization;
using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>
/// Reads a Prepare run's exit: its single <c>FOUNDRY_BASE</c> stdout line on success, or the bootstrap-failure
/// sentinel it prints on failure. The base comes only from this line, never from the Hand-off volume. A review run's
/// Prepare also prints its review packet facts on <c>FOUNDRY_REVIEW_PATH</c>, <c>FOUNDRY_REVIEW_LINES</c> and
/// <c>FOUNDRY_REVIEW_PATCH_ID</c> lines.
/// </summary>
internal static partial class PrepareOutputParser
{
    public const string MissingBaseMessage = "The Publisher finished without reporting the run's base.";

    public const string MalformedReviewPacketMessage = "The Publisher reported a malformed review packet.";

    private const string PathLinePrefix = "PATH ";
    private const string PatchIdLinePrefix = "PATCH_ID ";
    private const string LinesLinePrefix = "LINES ";

    public static PrepareOutcome Parse(long exitCode, string? log, bool expectsRunBranch, bool expectsReviewPacket = false)
    {
        string text = log ?? string.Empty;

        if (exitCode != 0)
        {
            return ContainerOutputParser.FindBootstrapSentinel(text) is { } sentinel
                ? new PrepareOutcome.Failed(BootstrapFailure(SecretRedactor.Redact(sentinel)))
                : new PrepareOutcome.Failed(new FailureReason.ContainerError(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The Publisher exited with code {exitCode} before preparing the run's base.")));
        }

        MatchCollection lines = BaseLinePattern().Matches(text);

        if (lines.Count != 1 || ReadBase(lines[0].Groups["fields"].Value, expectsRunBranch) is not { } runBase)
        {
            return new PrepareOutcome.Failed(new FailureReason.ContainerError(MissingBaseMessage));
        }

        MatchCollection reviewLines = ReviewLinePattern().Matches(text);

        if (!expectsReviewPacket)
        {
            return reviewLines.Count == 0
                ? new PrepareOutcome.Prepared(runBase)
                : new PrepareOutcome.Failed(new FailureReason.ContainerError(MalformedReviewPacketMessage));
        }

        return ReadReviewPacket(reviewLines) is { } reviewPacket
            ? new PrepareOutcome.Prepared(runBase, reviewPacket)
            : new PrepareOutcome.Failed(new FailureReason.ContainerError(MalformedReviewPacketMessage));
    }

    /// <summary>
    /// The Publisher retried its provider phase itself before printing <c>stage=unreachable</c> or <c>stage=fetch</c>, and
    /// <c>stage=refused</c> means the provider refused the token, so each of those sentinels is its own reason; every
    /// other sentinel is a bootstrap failure.
    /// </summary>
    private static FailureReason BootstrapFailure(string sentinel)
    {
        Match unreachable = UnreachableSentinelPattern().Match(sentinel);

        if (unreachable.Success)
        {
            return new FailureReason.ProviderUnreachable(unreachable.Groups["detail"].Value);
        }

        Match fetch = FetchSentinelPattern().Match(sentinel);

        if (fetch.Success)
        {
            return new FailureReason.FetchFailed(fetch.Groups["detail"].Value);
        }

        Match refused = RefusedSentinelPattern().Match(sentinel);

        return refused.Success
            ? new FailureReason.AccessRefused(refused.Groups["detail"].Value)
            : new FailureReason.WorkerBootstrapFailed(sentinel);
    }

    private static RunBase? ReadBase(string fields, bool expectsRunBranch)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);

        foreach (string field in fields.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = field.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0 || !values.TryAdd(field[..separator], field[(separator + 1)..]))
            {
                return null;
            }
        }

        if (!CommitSha.TryFrom(values.GetValueOrDefault("default_sha"), out CommitSha defaultBranchHead))
        {
            return null;
        }

        bool hasRunBranch = values.TryGetValue("branch_sha", out string? branchSha);

        if (hasRunBranch != expectsRunBranch)
        {
            return null;
        }

        if (!hasRunBranch)
        {
            return new RunBase(defaultBranchHead, RunBranchHead: null);
        }

        return CommitSha.TryFrom(branchSha, out CommitSha runBranchHead)
            ? new RunBase(defaultBranchHead, runBranchHead)
            : null;
    }

    /// <summary>
    /// Reads every <c>FOUNDRY_REVIEW_*</c> line, or returns null when any is malformed: a line is never skipped, so a
    /// packet that reached Foundry is the whole packet Prepare printed.
    /// </summary>
    private static ReviewPacketFacts? ReadReviewPacket(MatchCollection reviewLines)
    {
        List<ChangedPath> changedPaths = [];
        List<AddedLineRange> addedLines = [];
        PatchId? patchId = null;

        foreach (Match line in reviewLines)
        {
            string body = line.Groups["body"].Value;

            if (body.StartsWith(PathLinePrefix, StringComparison.Ordinal)
                && ReadChangedPath(body[PathLinePrefix.Length..]) is { } changedPath)
            {
                changedPaths.Add(changedPath);
            }
            else if (body.StartsWith(LinesLinePrefix, StringComparison.Ordinal)
                && ReadAddedLines(body[LinesLinePrefix.Length..]) is { } range)
            {
                addedLines.Add(range);
            }
            else if (body.StartsWith(PatchIdLinePrefix, StringComparison.Ordinal)
                && patchId is null
                && PatchId.TryFrom(body[PatchIdLinePrefix.Length..], out PatchId reported))
            {
                patchId = reported;
            }
            else
            {
                return null;
            }
        }

        // Prepare prints a patch-id only for a diff that changed something.
        if (patchId is not null && changedPaths.Count == 0)
        {
            return null;
        }

        // Prepare prints added lines only for a path its diff adds or modifies.
        if (addedLines.Any(range => !changedPaths.Any(path => path.Path == range.Path && path.Status != ChangeStatus.Deleted)))
        {
            return null;
        }

        return new ReviewPacketFacts(changedPaths, patchId, addedLines);
    }

    /// <summary>
    /// Reads <c>&lt;path&gt; &lt;start&gt;-&lt;end&gt;</c>. The range is everything after the last space, so a path holding
    /// spaces stays whole.
    /// </summary>
    private static AddedLineRange? ReadAddedLines(string pathAndRange)
    {
        int space = pathAndRange.LastIndexOf(' ');

        if (space <= 0)
        {
            return null;
        }

        Match range = LineRangePattern().Match(pathAndRange[(space + 1)..]);

        if (!range.Success
            || !int.TryParse(range.Groups["start"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int start)
            || !int.TryParse(range.Groups["end"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int end)
            || start < 1
            || end < start)
        {
            return null;
        }

        return new AddedLineRange(pathAndRange[..space], start, end);
    }

    /// <summary>
    /// Reads <c>&lt;status&gt;&lt;TAB&gt;&lt;path&gt;</c>. The path is everything after the first tab, so a path holding a tab or
    /// spaces stays whole.
    /// </summary>
    private static ChangedPath? ReadChangedPath(string statusAndPath)
    {
        int tab = statusAndPath.IndexOf('\t', StringComparison.Ordinal);

        if (tab != 1 || tab + 1 == statusAndPath.Length)
        {
            return null;
        }

        ChangeStatus? status = statusAndPath[0] switch
        {
            'A' => ChangeStatus.Added,
            'M' => ChangeStatus.Modified,
            'D' => ChangeStatus.Deleted,
            _ => null,
        };

        return status is { } known ? new ChangedPath(known, statusAndPath[(tab + 1)..]) : null;
    }

    // Anchored to the start of a line (after the optional Docker timestamp), like the bootstrap sentinel.
    [GeneratedRegex(
        @"^(?:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) )?FOUNDRY_BASE (?<fields>[^\r\n]*?)\r?$",
        RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex BaseLinePattern();

    [GeneratedRegex(
        @"^(?:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) )?FOUNDRY_REVIEW_(?<body>[^\r\n]*?)\r?$",
        RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReviewLinePattern();

    [GeneratedRegex(@"^(?<start>[0-9]{1,9})-(?<end>[0-9]{1,9})$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LineRangePattern();

    [GeneratedRegex(
        @"^FOUNDRY_BOOTSTRAP_FAILED stage=unreachable detail=(?:could not reach the repository: )?(?<detail>.*)$",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex UnreachableSentinelPattern();

    // The fetch step's prefix repeats the reason's summary, so it is dropped; the ls-remote step's is kept to name it.
    [GeneratedRegex(
        @"^FOUNDRY_BOOTSTRAP_FAILED stage=fetch detail=(?:fetching the repository failed: )?(?<detail>.*)$",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex FetchSentinelPattern();

    [GeneratedRegex(
        @"^FOUNDRY_BOOTSTRAP_FAILED stage=refused detail=(?<detail>.*)$",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex RefusedSentinelPattern();
}
