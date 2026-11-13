namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>
/// A pull request's state as one provider read reports it. <paramref name="HeadSha"/> is the head
/// commit at the time of the read, or null when the provider returned none.
/// </summary>
internal sealed record PullRequestStatus(bool IsClosed, bool IsMerged, string? HeadSha);
