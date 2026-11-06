using System.Text.RegularExpressions;

namespace Foundry.Modules.Workers.Features.Roles;

/// <summary>
/// The placeholders Foundry fills in a role's prompt templates. A role prompt that uses any other placeholder, or a
/// task prompt without <see cref="IssueNumber"/>, is rejected when the catalog loads and fails the unit tests.
/// </summary>
internal static partial class RolePromptPlaceholders
{
    public const string IssueNumber = "{issueNumber}";
    public const string IssueContent = "{issueContent}";

    /// <summary>Placeholders filled in <c>prompts/system.md</c>.</summary>
    public static readonly IReadOnlyList<string> SystemPrompt = [IssueNumber, IssueContent];

    /// <summary>Placeholders filled in <c>prompts/task.md</c>.</summary>
    public static readonly IReadOnlyList<string> TaskPrompt = [IssueNumber];

    /// <summary>Every <c>{name}</c> placeholder used in <paramref name="template"/>, in order of first use.</summary>
    public static IReadOnlyList<string> FindIn(string template) =>
        [.. PlaceholderPattern().Matches(template).Select(match => match.Value).Distinct(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{[A-Za-z][A-Za-z0-9]*\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlaceholderPattern();
}
