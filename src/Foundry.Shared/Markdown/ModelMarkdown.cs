using System.Text;
using System.Text.RegularExpressions;

namespace Foundry.Shared.Markdown;

/// <summary>Builds Markdown around untrusted text, such as model output derived from issue content, that Foundry posts publicly.</summary>
public static partial class ModelMarkdown
{
    private const char Backtick = '`';

    // Rendered through GitHub's and GitLab's markdown endpoints on 2026-11-18 (#1127): only a zero-width space after the
    // "@" stops the mention on both; a backslash or an "&#64;" entity still mentions on GitHub.
    private const char ZeroWidthSpace = '\u200B';

    private enum MarkdownContext
    {
        Inline,
        Block,
    }

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

    /// <summary>
    /// Makes model-written text safe to place inside Foundry's own Markdown: outside code spans every <c>@</c> is
    /// followed by a zero-width space so it mentions no one, <c>&lt;</c> and <c>&gt;</c> become entities so no HTML
    /// renders, and a line-leading block marker is escaped so the text cannot start a heading or list.
    /// </summary>
    public static string Inline(string text) => Sanitize(text, MarkdownContext.Inline);

    /// <summary>
    /// Makes model-written Markdown safe to post as a block: outside code spans and fenced code blocks, mentions and
    /// HTML are neutralized as in <see cref="Inline"/>, while block markers and line breaks stay as written so headings,
    /// lists and quotes still render.
    /// </summary>
    public static string Block(string markdown) => Sanitize(markdown, MarkdownContext.Block);

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

    private static string Sanitize(string text, MarkdownContext context)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder output = new(text.Length);
        string[] lines = text.Split('\n');
        Fence? openFence = null;

        // CommonMark pairs backtick runs across the lines of a paragraph. Once a line leaves a run unpaired, a later line
        // could close it, so code spans are not trusted again until a blank line or a fence ends the paragraph.
        bool backticksUnsettled = false;

        for (int index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                output.Append('\n');
            }

            string line = lines[index];

            if (openFence is { } fence)
            {
                output.Append(line);
                openFence = fence.IsClosedBy(line) ? null : fence;
                continue;
            }

            if (context == MarkdownContext.Block && Fence.OpenedBy(line) is { } opened)
            {
                output.Append(line);
                openFence = opened;
                backticksUnsettled = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                output.Append(line);
                backticksUnsettled = false;
                continue;
            }

            backticksUnsettled = AppendLine(output, line, context, backticksUnsettled);
        }

        return output.ToString();
    }

    // Returns whether the paragraph's backtick pairing is unsettled after this line.
    private static bool AppendLine(StringBuilder output, string line, MarkdownContext context, bool backticksUnsettled)
    {
        int position = AppendLineStart(output, line, context);

        while (position < line.Length)
        {
            char character = line[position];

            // Outside code a backslash makes the next backtick or backslash literal, so neither can open a code span.
            if (character == '\\' && position + 1 < line.Length && line[position + 1] is Backtick or '\\')
            {
                output.Append(line, position, 2);
                position += 2;
                continue;
            }

            if (character == Backtick)
            {
                int run = RunLength(line, position);
                int closing = backticksUnsettled ? -1 : ClosingRun(line, position + run, run);

                // A pipe inside a code span splits it when the line is a table row, leaving its content outside code.
                if (closing < 0 || line.AsSpan(position + run, closing - position - run).Contains('|'))
                {
                    output.Append(Backtick, run);
                    position += run;
                    backticksUnsettled = true;
                    continue;
                }

                output.Append(line, position, closing + run - position);
                position = closing + run;
                continue;
            }

            if (character == '&' && MentionEntity().Match(line, position) is { Success: true } entity)
            {
                output.Append(entity.Value).Append(ZeroWidthSpace);
                position += entity.Length;
                continue;
            }

            AppendEscaped(output, character);
            position++;
        }

        return backticksUnsettled;
    }

    // Copies the line's indentation, then escapes an inline block marker, or keeps block quote markers, which an entity
    // would stop from rendering. Returns the position after them.
    private static int AppendLineStart(StringBuilder output, string line, MarkdownContext context)
    {
        int position = AppendIndentation(output, line, 0);

        if (context == MarkdownContext.Block)
        {
            while (position < line.Length && line[position] == '>')
            {
                output.Append('>');
                position = AppendIndentation(output, line, position + 1);
            }

            return position;
        }

        if (position >= line.Length)
        {
            return position;
        }

        // A line-leading ">" needs no backslash: the entity it becomes starts no quote.
        if (line[position] is '#' or '-' or '+' or '*')
        {
            output.Append('\\').Append(line[position]);
            return position + 1;
        }

        int digitsEnd = position;
        while (digitsEnd < line.Length && char.IsAsciiDigit(line[digitsEnd]))
        {
            digitsEnd++;
        }

        if (digitsEnd > position && digitsEnd < line.Length && line[digitsEnd] is '.' or ')')
        {
            output.Append(line, position, digitsEnd - position).Append('\\').Append(line[digitsEnd]);
            return digitsEnd + 1;
        }

        return position;
    }

    private static int AppendIndentation(StringBuilder output, string line, int position)
    {
        while (position < line.Length && line[position] is ' ' or '\t')
        {
            output.Append(line[position]);
            position++;
        }

        return position;
    }

    private static void AppendEscaped(StringBuilder output, char character)
    {
        switch (character)
        {
            case '@':
                output.Append('@').Append(ZeroWidthSpace);
                break;
            case '<':
                output.Append("&lt;");
                break;
            case '>':
                output.Append("&gt;");
                break;
            default:
                output.Append(character);
                break;
        }
    }

    private static int RunLength(string line, int start)
    {
        int end = start;
        while (end < line.Length && line[end] == Backtick)
        {
            end++;
        }

        return end - start;
    }

    // The start of the next backtick run exactly as long as the opening one, or -1 when there is none.
    private static int ClosingRun(string line, int from, int length)
    {
        int position = from;
        while (position < line.Length)
        {
            if (line[position] != Backtick)
            {
                position++;
                continue;
            }

            int run = RunLength(line, position);
            if (run == length)
            {
                return position;
            }

            position += run;
        }

        return -1;
    }

    // Character references that decode to "@" still mention on both providers, so they get the zero-width space too.
    [GeneratedRegex(@"\G&(?:#0*64|#[xX]0*40|commat);", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MentionEntity();

    /// <summary>
    /// A fenced code block opened by an unindented fence. An indented fence can sit inside a list item or quote whose end
    /// also ends the fence, which this line-by-line reading cannot see, so its lines are sanitized as text instead.
    /// </summary>
    private readonly record struct Fence(char Marker, int Length)
    {
        public static Fence? OpenedBy(string line)
        {
            if (line.Length == 0 || line[0] is not (Backtick or '~'))
            {
                return null;
            }

            char marker = line[0];
            int length = RunOf(line, marker);

            // A backtick fence's info string cannot hold a backtick; such a line is inline code instead.
            bool isFence = length >= 3 && (marker != Backtick || !line.AsSpan(length).Contains(Backtick));
            return isFence ? new Fence(marker, length) : null;
        }

        public bool IsClosedBy(string line)
        {
            string trimmed = line.TrimStart(' ');
            if (line.Length - trimmed.Length > 3)
            {
                return false;
            }

            int length = RunOf(trimmed, Marker);
            return length >= Length && trimmed.AsSpan(length).IsWhiteSpace();
        }

        private static int RunOf(string line, char marker)
        {
            int length = 0;
            while (length < line.Length && line[length] == marker)
            {
                length++;
            }

            return length;
        }
    }
}
