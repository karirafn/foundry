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

    internal sealed record AuthInvalid : ContainerOutputParseResult;
}
