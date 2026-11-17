using System.Text.Json.Serialization;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// One kind of input that answered a clarification round (ADR 0096). Each kind is recorded as data, so no consumer has
/// to tell a status line apart from comment text.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Comments), "comments")]
[JsonDerivedType(typeof(DescriptionEdited), "descriptionEdited")]
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
}
