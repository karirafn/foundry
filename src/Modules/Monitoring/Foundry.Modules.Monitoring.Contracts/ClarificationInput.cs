using System.Text.Json.Serialization;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// What answered a clarification round (ADR 0096): the parts that arrived together, at least one.
/// </summary>
public sealed record ClarificationInput
{
    [JsonConstructor]
    public ClarificationInput(IReadOnlyList<InputPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        if (parts.Count == 0)
        {
            throw new ArgumentException("Clarification input holds at least one part.", nameof(parts));
        }

        Parts = [.. parts];
    }

    public IReadOnlyList<InputPart> Parts { get; }

    /// <summary>Input made only of the settled comments' text.</summary>
    public static ClarificationInput FromComments(string text) => new([new InputPart.Comments(text)]);

    public bool Equals(ClarificationInput? other) => other is not null && Parts.SequenceEqual(other.Parts);

    public override int GetHashCode() => Parts.Aggregate(0, (hash, part) => HashCode.Combine(hash, part));
}
