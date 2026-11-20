namespace Foundry.Modules.Workers.Contracts;

// The Credentials module's credit probe classifies its container output through the Workers module's internal parser,
// so the money-vs-time-vs-infrastructure discrimination has one source of truth.
public interface IProbeOutcomeClassifier
{
    ProbeOutcome Classify(string? logs);
}
