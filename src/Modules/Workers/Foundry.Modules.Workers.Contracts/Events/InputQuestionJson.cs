using System.Text.Json;
using System.Text.Json.Nodes;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// The stored JSON form of a list of <see cref="InputQuestion"/>s, shared by every column that holds questions. A
/// question stored before questions were structured carries no <c>$type</c> and reads as
/// <see cref="InputQuestion.Legacy"/>.
/// </summary>
public static class InputQuestionJson
{
    private const string TypeProperty = "$type";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
    };

    public static JsonNode ToNode(IReadOnlyList<InputQuestion> questions) =>
        JsonSerializer.SerializeToNode(questions, Options)
            ?? throw new InvalidOperationException("Questions serialised to null.");

    /// <exception cref="JsonException">A stored question cannot form an <see cref="InputQuestion"/>.</exception>
    /// <exception cref="ArgumentException">A stored structured question breaks its rules.</exception>
    public static IReadOnlyList<InputQuestion> FromNode(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        JsonArray tagged = new();

        foreach (JsonNode? item in array)
        {
            JsonNode? copy = item?.DeepClone();

            if (copy is JsonObject question && !question.ContainsKey(TypeProperty))
            {
                question[TypeProperty] = InputQuestion.LegacyDiscriminator;
            }

            tagged.Add(copy);
        }

        return tagged.Deserialize<List<InputQuestion>>(Options)
            ?? throw new JsonException("Stored questions deserialised to null.");
    }

    public static string Serialize(IReadOnlyList<InputQuestion> questions) => ToNode(questions).ToJsonString(Options);

    public static IReadOnlyList<InputQuestion> Deserialize(string json) => FromNode(JsonNode.Parse(json));
}
