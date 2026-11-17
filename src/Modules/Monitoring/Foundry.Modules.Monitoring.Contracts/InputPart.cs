using System.Text.Json.Serialization;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// One kind of input that answered a clarification round (ADR 0096). Each kind is recorded as data, so no consumer has
/// to tell a status line apart from comment text.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Comments), "comments")]
[JsonDerivedType(typeof(DescriptionEdited), "descriptionEdited")]
[JsonDerivedType(typeof(BranchUpdated), "branchUpdated")]
[JsonDerivedType(typeof(ContinuedWithoutAnswer), "continuedWithoutAnswer")]
public abstract record InputPart
{
    private InputPart()
    {
    }

    /// <summary>The settled comments on the question's thread, joined oldest first.</summary>
    public sealed record Comments(string Text) : InputPart
    {
        public string Text { get; } = Text ?? throw new ArgumentNullException(nameof(Text));
    }

    /// <summary>The issue description was edited after the question was posted, and the edit settled.</summary>
    public sealed record DescriptionEdited : InputPart;

    /// <summary>
    /// Someone moved the run's branch after the question was posted, and the move settled: from
    /// <paramref name="FromSha"/>, the head when the question was posted (<see langword="null"/> when the branch did not
    /// exist then), to <paramref name="ToSha"/>.
    /// </summary>
    public sealed record BranchUpdated(string? FromSha, string ToSha) : InputPart
    {
        public string ToSha { get; } = !string.IsNullOrWhiteSpace(ToSha)
            ? ToSha
            : throw new ArgumentException("The branch head a push moved to is required.", nameof(ToSha));
    }

    /// <summary>No answer arrived, and an operator chose to continue anyway; the run uses its own judgment.</summary>
    public sealed record ContinuedWithoutAnswer : InputPart;
}
