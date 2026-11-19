namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

/// <summary>
/// A GitHub repository permission the access probe sequence checks. Most are checked with a write
/// probe; <see cref="CommitStatuses"/> and <see cref="Actions"/> are read permissions checked with a read probe.
/// </summary>
internal enum WritePermission
{
    Contents,
    Issues,
    PullRequests,
    Administration,
    Webhooks,
    CommitStatuses,
    Actions,
}
