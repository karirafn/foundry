using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>
/// The JSON form of a <see cref="Toolchain"/>: an array of <c>{"name": "...", "version": "..."}</c>, the shape
/// <c>workers/toolchain.sh</c> prints and <c>worker_image_toolchains.entries</c> stores.
/// </summary>
internal static class ToolchainJson
{
    public static string Serialize(Toolchain toolchain)
    {
        ArgumentNullException.ThrowIfNull(toolchain);

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartArray();
            foreach (ToolchainEntry entry in toolchain.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Name);
                writer.WriteString("version", entry.Version);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The toolchain, or <see langword="null"/> when the JSON is malformed or breaks a toolchain rule.</summary>
    public static Toolchain? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return FromElement(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Toolchain? FromElement(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > Toolchain.MaxEntries)
        {
            return null;
        }

        List<ToolchainEntry> entries = [];

        foreach (JsonElement item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("name", out JsonElement name)
                || !item.TryGetProperty("version", out JsonElement version)
                || name.ValueKind != JsonValueKind.String
                || version.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            if (ToolchainEntry.TryCreate(name.GetString(), version.GetString()) is not { } entry)
            {
                return null;
            }

            entries.Add(entry);
        }

        return Toolchain.TryCreate(entries);
    }
}
