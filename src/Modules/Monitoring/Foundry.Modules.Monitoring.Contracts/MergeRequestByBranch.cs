namespace Foundry.Modules.Monitoring.Contracts;

public enum MergeRequestPresence
{
    None,
    Open,
    Merged,
    Closed,
}

public sealed record MergeRequestByBranch(MergeRequestPresence Presence, string? WebUrl);
