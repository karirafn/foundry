using Foundry.Modules.Workers.Contracts;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>
/// Scriptable in-memory fake of <see cref="IProbeOutcomeClassifier"/> for unit tests.
/// Returns the scripted outcome regardless of what logs are passed.
/// </summary>
internal sealed class FakeProbeOutcomeClassifier(ProbeOutcome outcome) : IProbeOutcomeClassifier
{
    public ProbeOutcome Classify(string? logs) => outcome;
}
