namespace Foundry.WebApi.Modules.Workers.Features;

public sealed class WorkerOptions
{
    public string Image { get; set; } = "ghcr.io/anthropics/claude-code:latest";

    public int MaxConcurrent { get; set; } = 3;

    public int TimeoutMinutes { get; set; } = 120;

    public string ConfigPath { get; set; } = "./workers/config";

    public string BranchNamingInstruction { get; set; } = "Use conventional branch naming";

    public string SystemPromptTemplate { get; set; } =
        """
        You are implementing GitHub issue #{issueNumber}: {title}.

        Issue body:
        {body}

        Branch naming: {branchNamingInstruction}.
        """;

    public string ReportsPath { get; set; } = "./data/reports";
}
