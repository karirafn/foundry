namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerSettingsOptions
{
    private static readonly IReadOnlyList<string> DefaultCiCdDenyRules =
    [
        "Edit(.github/workflows/**:*)",
        "Edit(.gitlab-ci.yml:*)",
        "Edit(Dockerfile:*)",
        "Edit(docker-compose*.yml:*)",
    ];

    public string? Model { get; set; }

    public IReadOnlyList<string> CiCdDenyRules { get; set; } = DefaultCiCdDenyRules;

    public IReadOnlyList<string> AdditionalDenyRules { get; set; } = [];

    public Dictionary<string, List<HookGroup>> Hooks { get; set; } = [];
}
