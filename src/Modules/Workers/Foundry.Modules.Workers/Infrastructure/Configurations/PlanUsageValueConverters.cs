using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

/// <summary>Stores a <see cref="PlanUsage"/> as <c>{"fiveHour":0.54,"sevenDay":0.47}</c>, a missing window as JSON null.</summary>
internal static class PlanUsageValueConverters
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // For an optional column: EF stores null without calling the converter, so the lambdas never see null.
    internal static readonly ValueConverter<PlanUsage?, string> OptionalConverter = new(
        usage => JsonSerializer.Serialize(usage, Options),
        json => Deserialize(json));

    private static PlanUsage Deserialize(string json) =>
        JsonSerializer.Deserialize<PlanUsage>(json, Options)
            ?? throw new InvalidOperationException("Failed to deserialize the stored plan usage.");
}
