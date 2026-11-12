using Foundry.Modules.Monitoring.Features.Providers;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

/// <summary>
/// One read of a GitLab merge request: its open/closed/merged status and the id and status of its
/// <c>head_pipeline</c>, both from the same <c>GET projects/:id/merge_requests/:iid</c> response. The
/// pipeline's failed jobs are not read here, so a status read stays a single request.
/// </summary>
internal sealed record MergeRequestSnapshot(PullRequestStatus Status, long? HeadPipelineId, string? HeadPipelineStatus);
