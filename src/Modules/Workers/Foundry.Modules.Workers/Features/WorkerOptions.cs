namespace Foundry.Modules.Workers.Features;

public sealed class WorkerOptions
{
    public string Image { get; set; } = "ghcr.io/anthropics/claude-code:latest";

    public string ApiKey { get; set; } = string.Empty;

    public int MaxConcurrent { get; set; } = 3;

    public int TimeoutMinutes { get; set; } = 120;

    public string ConfigPath { get; set; } = "./workers/config";

    public string BranchNamingInstruction { get; set; } = "Use conventional branch naming";

    public string SystemPromptTemplate { get; set; } =
        """
        You are implementing GitHub issue #{issueNumber}.

        {issueContent}

        Branch naming: {branchNamingInstruction}.
        """;

    public string ReportsPath { get; set; } = "./data/reports";

    /// <summary>Memory limit for worker containers in megabytes.</summary>
    public int MemoryLimitMb { get; set; } = 8192;

    /// <summary>CPU limit for worker containers (number of CPUs, fractional values allowed).</summary>
    public double CpuLimit { get; set; } = 2.0;

    /// <summary>Maximum number of processes (PIDs) per worker container.</summary>
    public int PidsLimit { get; set; } = 512;
}
