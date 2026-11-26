using System.Text.Json.Serialization;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Triage's judgment of an issue's implement work. It selects the Tier Model an implement run uses (ADR 0095); a
/// missing demand reads as <see cref="Demanding"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImplementDemand>))]
public enum ImplementDemand
{
    [JsonStringEnumMemberName("routine")]
    Routine,

    [JsonStringEnumMemberName("demanding")]
    Demanding,
}
