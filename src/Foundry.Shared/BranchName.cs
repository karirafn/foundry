using System.Text;
using System.Text.RegularExpressions;

namespace Foundry.Shared;

public readonly partial record struct BranchName(string Value)
{
    private const int MaxTotalLength = 100;

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumericPattern();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex ConsecutiveHyphensPattern();

    [GeneratedRegex(@"^[a-z][a-z0-9-]*$")]
    private static partial Regex ValidBranchPrefixPattern();

    public static BranchName From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Branch name must not be empty or whitespace.", nameof(value));
        }

        return new BranchName(value);
    }

    public static BranchName Generate(string branchPrefix, int issueNumber, string title)
    {
        if (!ValidBranchPrefixPattern().IsMatch(branchPrefix))
        {
            throw new ArgumentException(
                "Branch prefix must start with a lowercase letter and contain only lowercase letters, digits, and hyphens.",
                nameof(branchPrefix));
        }

        string prefix = $"{branchPrefix}/{issueNumber}";

        if (string.IsNullOrWhiteSpace(title))
        {
            return new BranchName(prefix);
        }

        string slug = Slugify(title);

        if (string.IsNullOrEmpty(slug))
        {
            return new BranchName(prefix);
        }

        string full = $"{prefix}-{slug}";

        if (full.Length > MaxTotalLength)
        {
            int maxSlugLength = MaxTotalLength - prefix.Length - 1;
            slug = slug[..maxSlugLength].TrimEnd('-');
            full = $"{prefix}-{slug}";
        }

        return new BranchName(full);
    }

    private static string Slugify(string title)
    {
        string ascii = RemoveNonAscii(title);
        string lower = ascii.ToLowerInvariant();
        string hyphenated = NonAlphanumericPattern().Replace(lower, "-");
        string collapsed = ConsecutiveHyphensPattern().Replace(hyphenated, "-");
        return collapsed.Trim('-');
    }

    private static string RemoveNonAscii(string input)
    {
        StringBuilder builder = new(input.Length);

        foreach (char c in input)
        {
            if (c < 128)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
