using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>
/// The log Foundry fetched for a failing CI job at dispatch. <see cref="Name"/> is the failing check's name,
/// <see cref="Text"/> the log's tail, which ends at byte <see cref="Length"/> of the log, and
/// <see cref="FailureExcerpt"/> the lines near the log's start that look like failures.
/// Fetched per dispatch and never persisted, so log text never reaches the stored revision trigger.
/// </summary>
internal sealed record FailingJobLog(string Name, string Text, long Length, IReadOnlyList<JobLogLine> FailureExcerpt);
