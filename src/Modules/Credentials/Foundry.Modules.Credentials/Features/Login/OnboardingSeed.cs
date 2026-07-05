using System.Text.Json;
using System.Text.Json.Nodes;

namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Produces the merged <c>.claude.json</c> content that suppresses Claude Code's
/// onboarding gates for a login container.
/// </summary>
/// <remarks>
/// All seed keys are set only when absent — existing values (including
/// <c>oauthAccount</c>) are never overwritten.
/// </remarks>
internal static class OnboardingSeed
{
    /// <summary>Default working directory inside the login container.</summary>
    internal const string DefaultWorkDir = "/home/node";

    private const string OnboardingKey = "hasCompletedOnboarding";
    private const string ThemeKey = "theme";
    private const string ProjectsKey = "projects";
    private const string TrustDialogKey = "hasTrustDialogAccepted";
    private const string ThemeDefault = "dark";

    /// <summary>
    /// Merges onboarding seed flags into <paramref name="existingJson"/>.
    /// </summary>
    /// <param name="existingJson">
    /// The current contents of <c>.claude.json</c> — may be <c>null</c>, empty,
    /// or malformed JSON. In all three cases, the method starts from a fresh object.
    /// </param>
    /// <param name="workDir">
    /// The trusted working directory path for the <c>hasTrustDialogAccepted</c> flag.
    /// </param>
    /// <returns>The merged JSON string.</returns>
    internal static string Merge(string? existingJson, string workDir)
    {
        JsonObject root = TryParseRoot(existingJson);

        SetIfAbsent(root, OnboardingKey, true);
        SetIfAbsent(root, ThemeKey, ThemeDefault);

        JsonObject projects = EnsureObject(root, ProjectsKey);
        JsonObject workDirEntry = EnsureObject(projects, workDir);
        SetIfAbsent(workDirEntry, TrustDialogKey, true);

        return root.ToJsonString();
    }

    private static JsonObject TryParseRoot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            JsonNode? node = JsonNode.Parse(json);
            return node is JsonObject obj ? obj : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void SetIfAbsent(JsonObject obj, string key, bool value)
    {
        if (!obj.ContainsKey(key))
        {
            obj[key] = value;
        }
    }

    private static void SetIfAbsent(JsonObject obj, string key, string value)
    {
        if (!obj.ContainsKey(key))
        {
            obj[key] = value;
        }
    }

    private static JsonObject EnsureObject(JsonObject parent, string key)
    {
        if (parent.TryGetPropertyValue(key, out JsonNode? existing) && existing is JsonObject existingObj)
        {
            return existingObj;
        }

        JsonObject newObj = [];
        parent[key] = newObj;
        return newObj;
    }
}
