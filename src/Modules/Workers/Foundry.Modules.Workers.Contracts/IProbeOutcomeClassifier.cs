namespace Foundry.Modules.Workers.Contracts;

public interface IProbeOutcomeClassifier
{
    ProbeOutcome Classify(string? logs);
}
