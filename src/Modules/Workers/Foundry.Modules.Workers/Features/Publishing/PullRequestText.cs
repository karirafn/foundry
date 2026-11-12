using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Foundry.Modules.Workers.Domain.ValueObjects;

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

    private const string RevertedFilesHeading = "## Files reverted by Foundry";

    private const string RevertedFilesExplanation =
        "Foundry's diff gate reverted these files in a separate commit before pushing, because binary files and files over its size limit are not published. Add them yourself if they belong in the change.";

    public static PullRequestText Build(
        PullRequestKind kind,
        PullRequestSummary? summary,
        PullRequestDescription? description,
        string issueTitle,
        int issueNumber,
        IReadOnlyList<string> commitSubjects,
        IReadOnlyList<RevertedFile> revertedFiles)
    {
        ArgumentNullException.ThrowIfNull(issueTitle);
        ArgumentNullException.ThrowIfNull(commitSubjects);
        ArgumentNullException.ThrowIfNull(revertedFiles);

        string title = $"{TypeOf(kind)}: {summary?.Value ?? SummaryFromIssueTitle(issueTitle, issueNumber)}";
        string body = SummaryComment(description, commitSubjects, revertedFiles)
            + "\n\nCloses #" + issueNumber.ToString(CultureInfo.InvariantCulture);

        return new PullRequestText(title, body);
    }

    /// <summary>
    /// The body of the summary comment posted on an existing pull request after a later run: the description, or the
    /// commit-subject fallback, then the files the Diff gate reverted. It carries no closing keyword, as the pull request
    /// body already links the issue.
    /// </summary>
    public static string SummaryComment(
        PullRequestDescription? description,
        IReadOnlyList<string> commitSubjects,
        IReadOnlyList<RevertedFile> revertedFiles)
    {
        ArgumentNullException.ThrowIfNull(commitSubjects);
        ArgumentNullException.ThrowIfNull(revertedFiles);

        string summary = description?.Value ?? BodyFromCommitSubjects(commitSubjects);
        return revertedFiles.Count == 0 ? summary : summary + "\n\n" + RevertedFilesSection(revertedFiles);
    }

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

    private static string BodyFromCommitSubjects(IReadOnlyList<string> commitSubjects)
    {
        string[] subjects = [.. commitSubjects
            .Select(subject => subject.Trim())
            .Where(subject => subject.Length > 0)];

        if (subjects.Length == 0)
        {
            return NoChangesListedSentence;
        }

        return "## Changes\n\n" + string.Join('\n', subjects.Select(subject => "- " + subject));
    }

    // The pull request is where a reviewer learns that the run's change is missing these files (#788).
    private static string RevertedFilesSection(IReadOnlyList<RevertedFile> revertedFiles)
    {
        StringBuilder section = new(RevertedFilesHeading);
        section.Append("\n\n").Append(RevertedFilesExplanation).Append('\n');

        foreach (RevertedFile file in revertedFiles)
        {
            section.Append("\n- ")
                .Append(CodeSpan(file.Path))
                .Append(": ")
                .Append(ReasonOf(file.Reason))
                .Append(" (")
                .Append(FileSize(file.SizeBytes))
                .Append(')');
        }

        return section.ToString();
    }

    private static string ReasonOf(RevertedFileReason reason) => reason switch
    {
        RevertedFileReason.Oversized => "too large",
        RevertedFileReason.Binary => "binary",
        _ => throw new UnreachableException($"Unhandled {nameof(RevertedFileReason)}: {reason}"),
    };

    // A fence one backtick longer than the longest run inside the path keeps it one code span.
    private static string CodeSpan(string text)
    {
        int longestRun = 0;
        int run = 0;
        foreach (char c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longestRun = Math.Max(longestRun, run);
        }

        string fence = new('`', longestRun + 1);
        string padding = text.StartsWith('`') || text.EndsWith('`') ? " " : string.Empty;
        return fence + padding + text + padding + fence;
    }

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
