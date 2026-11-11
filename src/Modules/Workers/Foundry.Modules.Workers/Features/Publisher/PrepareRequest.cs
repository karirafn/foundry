using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>
/// What Prepare fetches: the default branch always, and <paramref name="RunBranch"/> for a Continuation or Revision
/// run, whose branch already holds earlier work.
/// </summary>
internal sealed record PrepareRequest(
    WorkerRunId RunId,
    Uri CloneUrl,
    string GitPat,
    BranchName? RunBranch,
    WorkerResourceLimitsSnapshot Limits);
