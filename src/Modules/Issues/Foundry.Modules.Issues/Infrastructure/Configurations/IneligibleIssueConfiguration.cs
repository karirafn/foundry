using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class IneligibleIssueConfiguration : IEntityTypeConfiguration<IneligibleIssue>
{
    public void Configure(EntityTypeBuilder<IneligibleIssue> builder)
    {
        ValueConverter<IReadOnlyList<EligibilityViolation>, string> violationsConverter = new(
            violations => JsonSerializer.Serialize(
                violations.Select(v => new { v.Rule, v.Description }),
                (JsonSerializerOptions?)null),
            json => DeserializeViolations(json));

        ValueComparer<IReadOnlyList<EligibilityViolation>> violationsComparer = new(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            violations => violations.Aggregate(
                0,
                (hash, v) => HashCode.Combine(hash, v.Rule.GetHashCode(StringComparison.Ordinal))),
            violations => (IReadOnlyList<EligibilityViolation>)violations.ToList());

        builder.Property(i => i.Violations)
            .HasConversion(violationsConverter, violationsComparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("eligibility_violations");
    }

    private static IReadOnlyList<EligibilityViolation> DeserializeViolations(string json)
    {
        JsonArray? array = JsonSerializer.Deserialize<JsonArray>(json, (JsonSerializerOptions?)null);

        if (array is null)
        {
            return [];
        }

        List<EligibilityViolation> violations = [];

        foreach (JsonNode? node in array)
        {
            string? rule = node?["Rule"]?.GetValue<string>();
            string? description = node?["Description"]?.GetValue<string>();

            if (rule is not null && description is not null)
            {
                violations.Add(EligibilityViolation.From(rule, description));
            }
        }

        return violations;
    }
}
