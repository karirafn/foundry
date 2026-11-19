namespace Foundry.Shared.Markdown;

/// <summary>Builds Markdown around untrusted text, such as model output derived from issue content, that Foundry posts publicly.</summary>
public static class ModelMarkdown
{
    private const char Backtick = '`';

    /// <summary>Wraps <paramref name="text"/> in one CommonMark code span.</summary>
    /// <remarks>
    /// Inside a code span a mention, link, image or HTML tag stays literal text and cannot notify anyone or swallow later
    /// lines. A fence one backtick longer than the longest run inside the text keeps it one span, and a space pads text
    /// that starts or ends with a backtick so that backtick is not read as part of the fence.
    /// </remarks>
    public static string CodeSpan(string text)
    {
        string fence = new(Backtick, LongestBacktickRun(text) + 1);
        string padding = text.StartsWith(Backtick) || text.EndsWith(Backtick) ? " " : string.Empty;

        return $"{fence}{padding}{text}{padding}{fence}";
    }

    private static int LongestBacktickRun(string text)
    {
        int longest = 0;
        int current = 0;

        foreach (char character in text)
        {
            current = character == Backtick ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }
}
