namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The commits a run starts from (ADR 0100), as the Publisher's Prepare mode reported them: the default branch's head
/// and, for a Continuation or Revision run, the head of the run's existing branch.
/// </summary>
public sealed record RunBase(CommitSha DefaultBranchHead, CommitSha? RunBranchHead);
