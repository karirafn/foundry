using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Foundry.Shared;

/// <summary>
/// The fingerprint of the spec an issue body states: 64 lowercase hex characters of a SHA-256 Monitoring computes from
/// the body with every task-list marker normalized, so ticking a checkbox is not a rewrite. Compared only for equality.
/// </summary>
[JsonConverter(typeof(SpecHashJsonConverter))]
public sealed partial record SpecHash
{
    private SpecHash(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <exception cref="ArgumentException"><paramref name="value"/> is not 64 lowercase hex characters.</exception>
    public static SpecHash From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Pattern().IsMatch(value)
            ? new SpecHash(value)
            : throw new ArgumentException("A spec hash is 64 lowercase hex characters.", nameof(value));
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pattern();
}

public sealed class SpecHashJsonConverter : JsonConverter<SpecHash>
{
    public override SpecHash Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { } value)
        {
            throw new JsonException($"Expected a JSON string for SpecHash but got {reader.TokenType}.");
        }

        try
        {
            return SpecHash.From(value);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, SpecHash value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
