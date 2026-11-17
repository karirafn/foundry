namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>
/// An issue's state and body as one provider read reports them. <paramref name="Body"/> is the empty string when the
/// issue has no description.
/// </summary>
internal sealed record IssueStatus(bool IsClosed, string Body);
