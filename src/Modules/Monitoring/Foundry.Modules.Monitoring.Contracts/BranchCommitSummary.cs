namespace Foundry.Modules.Monitoring.Contracts;

public sealed record BranchCommitSummary(int CommitCount, string? LatestSha);
