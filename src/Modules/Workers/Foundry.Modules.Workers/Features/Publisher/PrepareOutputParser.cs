using System.Globalization;
using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>
/// Reads a Prepare run's exit: its single <c>FOUNDRY_BASE</c> stdout line on success, or the bootstrap-failure
/// sentinel it prints on failure. The base comes only from this line, never from the Hand-off volume.
/// </summary>
internal static partial class PrepareOutputParser
{
    public const string MissingBaseMessage = "The Publisher finished without reporting the run's base.";

    public static PrepareOutcome Parse(long exitCode, string? log, bool expectsRunBranch)
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

        return new PrepareOutcome.Prepared(runBase);
    }

    /// <summary>
    /// The Publisher retried an unreachable provider itself before printing <c>stage=unreachable</c>, so that sentinel
    /// is its own reason; every other sentinel is a bootstrap failure.
    /// </summary>
    private static FailureReason BootstrapFailure(string sentinel)
    {
        Match unreachable = UnreachableSentinelPattern().Match(sentinel);

        return unreachable.Success
            ? new FailureReason.ProviderUnreachable(unreachable.Groups["detail"].Value)
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

    // Anchored to the start of a line (after the optional Docker timestamp), like the bootstrap sentinel.
    [GeneratedRegex(
        @"^(?:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}) )?FOUNDRY_BASE (?<fields>[^\r\n]*?)\r?$",
        RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex BaseLinePattern();

    [GeneratedRegex(
        @"^FOUNDRY_BOOTSTRAP_FAILED stage=unreachable detail=(?:could not reach the repository: )?(?<detail>.*)$",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex UnreachableSentinelPattern();
}
