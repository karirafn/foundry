namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

/// <summary>
/// One read of a GitLab merge request: its raw <c>state</c>, head <c>sha</c>, draft flag, and the id and
/// status of its <c>head_pipeline</c>, all from the same <c>GET projects/:id/merge_requests/:iid</c> response.
/// The pipeline's failed jobs are not read here, so one read stays a single request.
/// </summary>
internal sealed record MergeRequestSnapshot(
    string? State,
    string? HeadSha,
    bool IsDraft,
    long? HeadPipelineId,
    string? HeadPipelineStatus);
