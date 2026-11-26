using Foundry.Modules.Credentials.Contracts;

namespace Foundry.Modules.Credentials.Features.Usage;

internal sealed class PlanUsageWindowReader : IPlanUsageWindowReader
{
    public PlanUsageWindows Read(IEnumerable<string> jsonLines)
    {
        ArgumentNullException.ThrowIfNull(jsonLines);
        return ProbeTurnUsageParser.ReadWindows(jsonLines);
    }
}
