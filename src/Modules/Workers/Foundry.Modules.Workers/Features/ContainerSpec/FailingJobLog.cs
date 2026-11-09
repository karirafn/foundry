namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>
/// The log text Foundry fetched for a failing CI job at dispatch. <see cref="Name"/> is the failing check's name.
/// Fetched per dispatch and never persisted, so log text never reaches the stored revision trigger.
/// </summary>
internal sealed record FailingJobLog(string Name, string Text);
