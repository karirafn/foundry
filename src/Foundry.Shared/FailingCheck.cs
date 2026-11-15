namespace Foundry.Shared;

/// <summary>
/// A single provider check that failed at the time the outcome was recorded.
/// </summary>
/// <param name="IsInfrastructureFailure">
/// The check failed before its script ran (e.g. GitLab <c>ci_quota_exceeded</c>), so a fix run has nothing to fix.
/// Defaults to <see langword="false"/> so checks recorded before the flag existed read as fixable.
/// </param>
public sealed record FailingCheck(string Name, string Conclusion, string? DetailsUrl, bool IsInfrastructureFailure = false);
