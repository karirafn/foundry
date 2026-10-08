using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// Persisted as JSON in the EF eligibility column. Only <c>rule</c> is stored; descriptions are
/// derived at read time from the rule and provider type so copy changes propagate immediately without
/// requiring a database migration or re-check. Legacy rows that also stored a <c>description</c> field
/// are tolerated on deserialization — the converter reads <c>rule</c> and skips all other properties.
/// </summary>
[JsonConverter(typeof(EligibilityViolationConverter))]
public sealed record EligibilityViolation(string Rule)
{
    public static readonly string AllowDirectPushesRule = EligibilityViolationInfo.AllowDirectPushesRule;
    public static readonly string AllowForcePushesRule = EligibilityViolationInfo.AllowForcePushesRule;
    public static readonly string AllowDeletionRule = EligibilityViolationInfo.AllowDeletionRule;

    public static EligibilityViolation AllowDirectPushes() =>
        new(AllowDirectPushesRule);

    public static EligibilityViolation AllowForcePushes() =>
        new(AllowForcePushesRule);

    public static EligibilityViolation AllowDeletion() =>
        new(AllowDeletionRule);

    public static EligibilityViolation NoCredential(string namespaceName) =>
        new(EligibilityViolationInfo.NoCredentialRule(namespaceName));

    public static EligibilityViolation CannotPush(string slug) =>
        new(EligibilityViolationInfo.CannotPushRule(slug));

    public static EligibilityViolation CannotPush(RepositorySlug slug) =>
        CannotPush(slug.ToString());
}

/// <summary>
/// Reads only the <c>rule</c> property and skips everything else (including the legacy
/// <c>description</c> field). Writes only <c>rule</c> so the stored JSON stays minimal.
/// </summary>
internal sealed class EligibilityViolationConverter : JsonConverter<EligibilityViolation>
{
    public override EligibilityViolation? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object.");
        }

        string? rule = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string propertyName = reader.GetString()!;
            reader.Read();

            if (propertyName.Equals("rule", StringComparison.OrdinalIgnoreCase))
            {
                rule = reader.GetString();
            }
            else
            {
                // Skip unknown / legacy properties (e.g. the old "description" field).
                reader.Skip();
            }
        }

        return rule is null ? null : new EligibilityViolation(rule);
    }

    public override void Write(
        Utf8JsonWriter writer,
        EligibilityViolation value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("rule", value.Rule);
        writer.WriteEndObject();
    }
}
