namespace Foundry.Modules.Monitoring.Features.Providers.TrackerReads;

/// <summary>
/// Bounds the tracker text that the never-shed minimal form of the tracker context renders. A hostile issue can
/// carry an unbounded label set or title, and the scaffold around the prompt has a fixed ceiling; a tracker read
/// must never push a dispatch past it, so the text is capped where it is collected, whichever provider read it.
/// </summary>
internal static class TrackerTextLimits
{
    public const int MaxLabels = 20;

    public const int MaxTextLength = 256;

    public static IReadOnlyList<string> CapLabels(IReadOnlyList<string> labels) =>
    [
        .. labels
            .Take(MaxLabels)
            .Select(CapText),
    ];

    public static string CapText(string text)
    {
        if (text.Length <= MaxTextLength)
        {
            return text;
        }

        int length = char.IsHighSurrogate(text[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;

        return text[..length];
    }
}
