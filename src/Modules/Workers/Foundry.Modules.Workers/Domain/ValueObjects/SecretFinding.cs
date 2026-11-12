namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// A possible secret the Diff gate's scanner found in a Run commit (#788). It locates the finding and names the rule
/// that matched; it has no member for the matched value, so the value can never reach the database, an event, a log
/// or the dashboard.
/// </summary>
public sealed record SecretFinding
{
    public SecretFinding(string path, int line, CommitSha commit, string ruleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);

        Path = path;
        Line = line;
        Commit = commit;
        RuleId = ruleId;
    }

    /// <summary>The file the finding is in, relative to the repository root.</summary>
    public string Path { get; }

    /// <summary>The 1-based line the finding starts on.</summary>
    public int Line { get; }

    /// <summary>The Run commit, as rewritten for publishing, that added the finding.</summary>
    public CommitSha Commit { get; }

    /// <summary>The scanner rule that matched, e.g. <c>aws-access-token</c>.</summary>
    public string RuleId { get; }
}
