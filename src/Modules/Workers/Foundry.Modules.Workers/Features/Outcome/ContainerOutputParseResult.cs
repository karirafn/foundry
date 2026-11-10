namespace Foundry.Modules.Workers.Features.Outcome;

internal abstract record ContainerOutputParseResult
{
    private ContainerOutputParseResult()
    {
    }

    internal sealed record NormalExit : ContainerOutputParseResult;

    internal sealed record UsageLimited(DateTimeOffset ResetsAt) : ContainerOutputParseResult;

    internal sealed record ParseFailure(string RawOutput) : ContainerOutputParseResult;

    internal sealed record WorkerBootstrapFailed(string Detail) : ContainerOutputParseResult;

    internal sealed record NoResultLine : ContainerOutputParseResult;

    // claude emitted at least one stream-json event, so bootstrap passed, but no result line followed.
    internal sealed record AgentStartedNoResultLine : ContainerOutputParseResult;

    internal sealed record AuthInvalid : ContainerOutputParseResult;

    internal sealed record TransientApiError : ContainerOutputParseResult;

    internal sealed record CreditsExhausted : ContainerOutputParseResult;

    internal sealed record ModelUnavailable : ContainerOutputParseResult;

    internal sealed record TurnLimitReached : ContainerOutputParseResult;
}
