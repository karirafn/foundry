using System.Globalization;

namespace Foundry.Modules.Workers.Features;

internal static class SystemPromptBuilder
{
    public static string Build(int issueNumber, string title, string body, WorkerOptions options)
    {
        return options.SystemPromptTemplate
            .Replace("{issueNumber}", issueNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{title}", title, StringComparison.Ordinal)
            .Replace("{body}", body, StringComparison.Ordinal)
            .Replace("{branchNamingInstruction}", options.BranchNamingInstruction, StringComparison.Ordinal);
    }
}
