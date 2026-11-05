using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Settings.Contracts;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

internal static class WorkerSettingsBuilder
{
    private static readonly string[] BaseDenyList =
    [
        "Bash(git push --force:*)",
        "Bash(git push * main)",
        "Bash(git push * master)",
        "Bash(npm publish:*)",
        "Bash(npx -y:*)",
        "Bash(git branch -D:*)",
        "Bash(git branch -d:*)",
        "Bash(git push --delete:*)",
        "Bash(git push * HEAD:*)",
        "Bash(git push * :*)",
    ];

    private static readonly string[] CiCdDenyList =
    [
        "Edit(**/.github/workflows/**)",
        "Edit(**/.gitlab-ci.yml)",
        "Edit(**/Dockerfile)",
        "Edit(**/docker-compose*.yml)",
        "Edit(**/docker-compose.yaml)",
        "Edit(**/compose.yml)",
        "Edit(**/compose.yaml)",
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Hooks are not emitted here: the entrypoint merges them from the host settings file at container start.
    public static string Build(ClaudeCodeSettingsSnapshot claudeCode)
    {
        string[] denyRules =
        [
            ..BaseDenyList,
            ..(claudeCode.BlockCiCdEdits ? CiCdDenyList : []),
            ..claudeCode.AdditionalDenyRules,
        ];

        SettingsDocument document = new()
        {
            Model = string.IsNullOrWhiteSpace(claudeCode.Model) ? null : claudeCode.Model,
            Permissions = new() { Deny = denyRules },
            Attribution = new(),
        };

        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    private sealed class SettingsDocument
    {
        public string? Model { get; set; }

        public required PermissionsSection Permissions { get; set; }

        public required AttributionSection Attribution { get; set; }
    }

    // A worker never adds AI attribution; managed settings make this unoverridable. The object form is used
    // rather than `"attribution": false` because CLIs before v2.1.281 reject the boolean and skip the whole file.
    private sealed class AttributionSection
    {
        public string Commit { get; } = string.Empty;

        public string Pr { get; } = string.Empty;

        public bool SessionUrl { get; }
    }

    private sealed class PermissionsSection
    {
        public required string[] Deny { get; set; }
    }
}
