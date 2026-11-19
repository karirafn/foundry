using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared.Markdown;

namespace Foundry.Modules.Workers.Features.Publishing;

/// <summary>The kind of change a pull request carries; it picks the conventional-commit type of the title.</summary>
internal enum PullRequestKind
{
    Feature,
    Bug,
    Refactor,
    Documentation,
    Unclassified,
}

/// <summary>
/// The title and body of the pull request Foundry opens for a published run (ADR 0100). Code writes them from the
/// verdict's scrubbed <see cref="PullRequestSummary"/> and <see cref="PullRequestDescription"/>, falling back to the
/// issue title and the run's commit subjects when either is missing.
/// </summary>
internal sealed partial record PullRequestText(string Title, string Body)
{
    /// <summary>The body when neither a description nor any commit subject is available.</summary>
    internal const string NoChangesListedSentence = "This pull request carries the changes a Foundry worker run made for the issue.";

    private const string Ellipsis = "…";

    private const string ImplementationNotesSummary = "Implementation notes";

    public static PullRequestText Build(
        PullRequestKind kind,
        PullRequestSummary? summary,
        PullRequestDescription? description,
        string issueTitle,
        int issueNumber,
        IReadOnlyList<string> commitSubjects,
        IReadOnlyList<RevertedFile> revertedFiles,
        IReadOnlyList<SecretFinding> genericFindings,
        FalsePositiveConfirmation? confirmation = null)
    {
        ArgumentNullException.ThrowIfNull(issueTitle);
        ArgumentNullException.ThrowIfNull(commitSubjects);
        ArgumentNullException.ThrowIfNull(revertedFiles);
        ArgumentNullException.ThrowIfNull(genericFindings);

        string title = $"{TypeOf(kind)}: {summary?.Value ?? SummaryFromIssueTitle(issueTitle, issueNumber)}";
        string body = Compose(heading: null, description, commitSubjects, revertedFiles, genericFindings, confirmation)
            + "\n\nCloses #" + issueNumber.ToString(CultureInfo.InvariantCulture);

        return new PullRequestText(title, body);
    }

    /// <summary>
    /// The body of the summary comment posted on an existing pull request after a later run: laid out as the pull request
    /// body, under a heading naming <paramref name="runKind"/> (an <c>IssueSummary</c> run kind; none when it is
    /// <see langword="null"/>). It carries no closing keyword, as the pull request body already links the issue.
    /// </summary>
    public static string SummaryComment(
        string? runKind,
        PullRequestDescription? description,
        IReadOnlyList<string> commitSubjects,
        IReadOnlyList<RevertedFile> revertedFiles,
        IReadOnlyList<SecretFinding> genericFindings,
        FalsePositiveConfirmation? confirmation = null)
    {
        ArgumentNullException.ThrowIfNull(commitSubjects);
        ArgumentNullException.ThrowIfNull(revertedFiles);
        ArgumentNullException.ThrowIfNull(genericFindings);

        return Compose(HeadingOf(runKind), description, commitSubjects, revertedFiles, genericFindings, confirmation);
    }

    // One alert leads with everything a reviewer must check; the diff gate's lists sit collapsed below, as their alert
    // lines already carry the counts. Blank lines surround each <details> body because GitLab renders Markdown inside it
    // only then.
    private static string Compose(
        string? heading,
        PullRequestDescription? description,
        IReadOnlyList<string> commitSubjects,
        IReadOnlyList<RevertedFile> revertedFiles,
        IReadOnlyList<SecretFinding> genericFindings,
        FalsePositiveConfirmation? confirmation)
    {
        List<string> blocks = [];

        if (Alert(description?.Attention ?? [], revertedFiles.Count, genericFindings.Count) is { } alert)
        {
            blocks.Add(alert);
        }

        string? cause = description?.Cause is { } text ? "**Cause:** " + ModelMarkdown.Inline(text) : null;
        if (heading is not null || cause is not null)
        {
            blocks.Add(string.Join('\n', new[] { heading is null ? null : "### " + heading, cause }.OfType<string>()));
        }

        if (description is null)
        {
            blocks.Add(ChangesFromCommitSubjects(commitSubjects));
        }
        else
        {
            blocks.Add(BulletList("**Changes**", description.Changes));
            blocks.Add(BulletList("**Verified**", description.Verification));

            if (description.Notes is { } notes)
            {
                blocks.Add(Details(ImplementationNotesSummary, ModelMarkdown.Block(notes)));
            }
        }

        if (revertedFiles.Count > 0)
        {
            blocks.Add(Details(
                $"Reverted files ({revertedFiles.Count.ToString(CultureInfo.InvariantCulture)})",
                string.Join('\n', revertedFiles.Select(RevertedFileItem))));
        }

        if (genericFindings.Count > 0)
        {
            blocks.Add(Details(
                $"Possible secrets ({genericFindings.Count.ToString(CultureInfo.InvariantCulture)})",
                FindingItems(genericFindings)));
        }

        if (confirmation is not null)
        {
            blocks.Add(ConfirmedFindingsDetails(confirmation));
        }

        return string.Join("\n\n", blocks);
    }

    // Confirmed false positives get no alert line: a person already decided them, so they are a record.
    private static string? Alert(IReadOnlyList<string> attention, int revertedFileCount, int genericFindingCount)
    {
        List<string> lines = [];

        if (revertedFileCount > 0)
        {
            lines.Add(revertedFileCount == 1
                ? "1 file was reverted by Foundry's diff gate and is missing from this change."
                : $"{revertedFileCount.ToString(CultureInfo.InvariantCulture)} files were reverted by Foundry's diff gate and are missing from this change.");
        }

        if (genericFindingCount > 0)
        {
            lines.Add(genericFindingCount == 1
                ? "1 line matched a generic secret pattern; check it is not a real secret."
                : $"{genericFindingCount.ToString(CultureInfo.InvariantCulture)} lines matched a generic secret pattern; check none is a real secret.");
        }

        lines.AddRange(attention.Select(ModelMarkdown.Inline));

        // GitHub recognises the alert tag only on a line of its own.
        return lines.Count == 0 ? null : "> [!WARNING]\n" + string.Join('\n', lines.Select(line => "> - " + line));
    }

    private static string? HeadingOf(string? runKind) => runKind switch
    {
        IssueSummary.ChecksFixRunKind => "Checks fix",
        IssueSummary.ReviewFixRunKind => "Review fix",
        IssueSummary.FindingsFixRunKind => "Review findings fix",
        IssueSummary.ConflictFixRunKind => "Conflict fix",
        IssueSummary.InputResumeRunKind => "Resumed with input",
        IssueSummary.ContinuationRunKind => "Continuation",
        IssueSummary.FreshRunKind => "Update",
        _ => null,
    };

    private static string BulletList(string label, IEnumerable<string> items) =>
        label + "\n" + string.Join('\n', items.Select(item => "- " + ModelMarkdown.Inline(item)));

    private static string Details(string summary, string body) =>
        $"<details><summary>{summary}</summary>\n\n{body}\n\n</details>";

    private static string TypeOf(PullRequestKind kind) => kind switch
    {
        PullRequestKind.Feature => "feat",
        PullRequestKind.Bug => "fix",
        PullRequestKind.Refactor => "refactor",
        PullRequestKind.Documentation => "docs",
        PullRequestKind.Unclassified => "feat",
        _ => throw new UnreachableException($"Unhandled {nameof(PullRequestKind)}: {kind}"),
    };

    // The issue title stands in for a missing summary, so it is held to the same one-line length limit.
    private static string SummaryFromIssueTitle(string issueTitle, int issueNumber)
    {
        string collapsed = Whitespace().Replace(issueTitle, " ").Trim();

        if (collapsed.Length == 0)
        {
            return $"resolve issue #{issueNumber.ToString(CultureInfo.InvariantCulture)}";
        }

        if (PullRequestSummary.TryCreate(collapsed) is { } fits)
        {
            return fits.Value;
        }

        string cut = TruncateToRunes(collapsed, PullRequestSummary.MaxLength - Ellipsis.Length);
        int lastSpace = cut.LastIndexOf(' ');

        // Cut at the last whole word unless that throws away most of the room.
        if (lastSpace > cut.Length / 2)
        {
            cut = cut[..lastSpace];
        }

        return cut.TrimEnd() + Ellipsis;
    }

    private static string TruncateToRunes(string value, int maxRunes)
    {
        int length = 0;
        int runes = 0;

        foreach (Rune rune in value.EnumerateRunes())
        {
            if (runes == maxRunes)
            {
                break;
            }

            length += rune.Utf16SequenceLength;
            runes++;
        }

        return value[..length];
    }

    private static string ChangesFromCommitSubjects(IReadOnlyList<string> commitSubjects)
    {
        string[] subjects = [.. commitSubjects
            .Select(subject => subject.Trim())
            .Where(subject => subject.Length > 0)];

        return subjects.Length == 0 ? NoChangesListedSentence : BulletList("**Changes**", subjects);
    }

    private static string RevertedFileItem(RevertedFile file) =>
        $"- {ModelMarkdown.CodeSpan(file.Path)}: {ReasonOf(file.Reason)} ({FileSize(file.SizeBytes)})";

    // The pull request is the lasting record that a person, not the scanner, let these findings through. It names where
    // and which rule only, because a finding never holds a matched value.
    private static string ConfirmedFindingsDetails(FalsePositiveConfirmation confirmation)
    {
        string confirmedAt = confirmation.ConfirmedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        return Details(
            $"Confirmed false positives ({confirmation.Findings.Count.ToString(CultureInfo.InvariantCulture)})",
            $"An operator confirmed these possible secrets as false positives on {confirmedAt} UTC, so Foundry published them.\n\n"
                + FindingItems(confirmation.Findings));
    }

    private static string FindingItems(IReadOnlyList<SecretFinding> findings) =>
        string.Join('\n', findings.Select(finding =>
            $"- {ModelMarkdown.CodeSpan(finding.Path)} line {finding.Line.ToString(CultureInfo.InvariantCulture)}: rule {ModelMarkdown.CodeSpan(finding.RuleId)}"));

    private static string ReasonOf(RevertedFileReason reason) => reason switch
    {
        RevertedFileReason.Oversized => "too large",
        RevertedFileReason.Binary => "binary",
        _ => throw new UnreachableException($"Unhandled {nameof(RevertedFileReason)}: {reason}"),
    };

    // Base 1024, one decimal, a trailing ".0" dropped: 940 B, 12.4 KB, 3.1 MB.
    private static string FileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        string number = unit == 0
            ? bytes.ToString(CultureInfo.InvariantCulture)
            : Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);
        return number + " " + units[unit];
    }

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Whitespace();
}
