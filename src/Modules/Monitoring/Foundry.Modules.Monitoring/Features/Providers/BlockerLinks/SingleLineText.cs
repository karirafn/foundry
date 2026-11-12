namespace Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;

/// <summary>
/// Reduces text from a provider response or a model verdict to one capped line, so it cannot forge further
/// lines of the comment or message that carries it.
/// </summary>
internal static class SingleLineText
{
    /// <summary>The longest text a blocker comment or recorded provider message carries.</summary>
    public const int MaxLength = 500;

    private const string Ellipsis = "…";

    public static string Cap(string text, int maxLength = MaxLength)
    {
        string flattened = text.Trim().ReplaceLineEndings(" ");

        if (flattened.Length <= maxLength)
        {
            return flattened;
        }

        int keep = maxLength - Ellipsis.Length;

        if (char.IsHighSurrogate(flattened[keep - 1]))
        {
            keep--;
        }

        return string.Concat(flattened.AsSpan(0, keep), Ellipsis);
    }
}
