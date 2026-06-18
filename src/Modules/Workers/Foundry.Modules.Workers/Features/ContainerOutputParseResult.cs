namespace Foundry.Modules.Workers.Features;

internal abstract record ContainerOutputParseResult
{
    private ContainerOutputParseResult()
    {
    }

    internal sealed record NormalExit : ContainerOutputParseResult;

    internal sealed record UsageLimited(DateTimeOffset ResetsAt) : ContainerOutputParseResult;

    internal sealed record ParseFailure(string RawOutput) : ContainerOutputParseResult;
}
