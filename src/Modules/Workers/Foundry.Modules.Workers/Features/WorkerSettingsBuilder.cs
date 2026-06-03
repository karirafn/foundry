using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundry.Modules.Workers.Features;

internal static class WorkerSettingsBuilder
{
    private static readonly string[] BaseDenyList =
    [
        "Bash(git push --force:*)",
        "Bash(git push * main)",
        "Bash(git push * master)",
        "Bash(npm publish:*)",
        "Bash(npx -y:*)",
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Build(WorkerSettingsOptions? settings)
    {
        string[] denyRules = settings?.AdditionalDenyRules.Count > 0
            ? [..BaseDenyList, ..settings.AdditionalDenyRules]
            : BaseDenyList;

        SettingsDocument document = new()
        {
            Model = settings?.Model,
            Permissions = new() { Deny = denyRules },
            Hooks = settings?.Hooks.Count > 0 ? settings.Hooks : null,
        };

        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    private sealed class SettingsDocument
    {
        public string? Model { get; set; }

        public required PermissionsSection Permissions { get; set; }

        public Dictionary<string, List<HookGroup>>? Hooks { get; set; }
    }

    private sealed class PermissionsSection
    {
        public required string[] Deny { get; set; }
    }
}
