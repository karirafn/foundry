using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>
/// The commit a review run reads (ADR 0114), and the pull request's source branch Prepare fetches so the commit is
/// reachable.
/// </summary>
internal sealed record ReviewTarget(BranchName Branch, CommitSha Commit);
