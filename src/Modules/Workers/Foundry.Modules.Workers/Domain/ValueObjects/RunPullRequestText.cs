namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The pull request text an implement run's verdict carried (<c>pullRequest: { summary, body }</c>), as the run wrote
/// it. It is validated and scrubbed where it is used, so a later fix to the rules applies to runs already waiting.
/// </summary>
public sealed record RunPullRequestText(string? Summary, string? Body);
