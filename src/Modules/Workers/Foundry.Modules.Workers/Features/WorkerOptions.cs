using Foundry.Modules.Workers.Features.ImageBuild;

namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerOptions
{
    public string Image { get; set; } = "ghcr.io/anthropics/claude-code";

    public IReadOnlyDictionary<string, string> Mounts { get; set; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> WritableMounts { get; set; } = new Dictionary<string, string>();

    public WorkerSettingsOptions Settings { get; set; } = new();

    public ImageBuildOptions ImageBuild { get; set; } = new();

    public string BranchNamingInstruction { get; set; } = "Use conventional branch naming";

    public string SystemPromptTemplate { get; set; } =
        """
        You are implementing GitHub issue #{issueNumber}.

        {issueContent}

        Branch naming: {branchNamingInstruction}.
        """;

    public string WorkerPromptTemplate { get; set; } =
        "Implement GitHub issue #{issueNumber}. Create a feature branch, make the changes, commit, and push to the remote. Do not create a pull request unless explicitly instructed.";

    public string ReportsPath { get; set; } = "./data/reports";

    /// <summary>Memory limit for worker containers in megabytes.</summary>
    public int MemoryLimitMb { get; set; } = 8192;

    /// <summary>CPU limit for worker containers (number of CPUs, fractional values allowed).</summary>
    public double CpuLimit { get; set; } = 2.0;

    /// <summary>Maximum number of processes (PIDs) per worker container.</summary>
    public int PidsLimit { get; set; } = 512;
}
