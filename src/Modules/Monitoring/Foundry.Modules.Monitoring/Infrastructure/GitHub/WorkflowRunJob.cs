namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

/// <summary>A job of a GitHub Actions workflow run, as listed by the run's jobs endpoint.</summary>
internal sealed record WorkflowRunJob(long Id, string Name, string? HtmlUrl);
