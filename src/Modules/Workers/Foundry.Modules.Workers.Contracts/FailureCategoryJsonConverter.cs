using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed class FailureCategoryJsonConverter : JsonConverter<FailureCategory>
{
    public override FailureCategory Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected a JSON string for {nameof(FailureCategory)} but got {reader.TokenType}.");
        }

        string? token = reader.GetString();

        if (token is null)
        {
            throw new JsonException($"Expected a non-null string for {nameof(FailureCategory)}.");
        }

        Result<FailureCategory> result = FailureCategory.Create(token);

        if (result is Result<FailureCategory>.Success success)
        {
            return success.Value;
        }

        throw new JsonException($"Unknown failure category token '{token}'.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        FailureCategory value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
