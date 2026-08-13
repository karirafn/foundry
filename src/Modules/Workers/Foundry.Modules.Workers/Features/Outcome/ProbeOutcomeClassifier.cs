using System.Diagnostics;

using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Features.Outcome;

internal sealed class ProbeOutcomeClassifier(IContainerOutputParser parser) : IProbeOutcomeClassifier
{
    public ProbeOutcome Classify(string? logs)
    {
        ContainerOutputParseResult result = parser.Parse(logs);

        return result switch
        {
            ContainerOutputParseResult.NormalExit => new ProbeOutcome.Available(),
            ContainerOutputParseResult.CreditsExhausted => new ProbeOutcome.CreditsStillBlocked(),
            ContainerOutputParseResult.UsageLimited usageLimited => new ProbeOutcome.UsageLimited(usageLimited.ResetsAt),
            ContainerOutputParseResult.AuthInvalid => new ProbeOutcome.InfrastructureFailure(),
            ContainerOutputParseResult.TransientApiError => new ProbeOutcome.InfrastructureFailure(),
            ContainerOutputParseResult.ParseFailure => new ProbeOutcome.InfrastructureFailure(),
            ContainerOutputParseResult.NoResultLine => new ProbeOutcome.InfrastructureFailure(),
            ContainerOutputParseResult.WorkerBootstrapFailed => new ProbeOutcome.InfrastructureFailure(),
            _ => throw new UnreachableException($"Unhandled {nameof(ContainerOutputParseResult)}: {result}"),
        };
    }
}
