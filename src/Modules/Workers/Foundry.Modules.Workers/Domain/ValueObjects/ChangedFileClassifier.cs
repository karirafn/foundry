namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// Classes a repository path as <see cref="ChangedFileClass.Test"/>, <see cref="ChangedFileClass.Other"/> or
/// <see cref="ChangedFileClass.Production"/> by its directories, name and extension alone (ADR 0114). The rules are
/// checked in that order, so a test under <c>docs/</c> is a test. Code, not the reviewer, computes the class, so a
/// review is judged against the same facts it was shown.
/// </summary>
public static class ChangedFileClassifier
{
    private static readonly HashSet<string> TestDirectories =
        new(["test", "tests", "__tests__", "spec"], StringComparer.OrdinalIgnoreCase);

    private static readonly string[] TestProjectSuffixes = [".Tests", ".UnitTests", ".IntegrationTests"];

    private static readonly HashSet<string> OtherExtensions =
        new([".md", ".txt", ".png", ".jpg", ".gif", ".svg"], StringComparer.OrdinalIgnoreCase);

    private const string DocsDirectory = "docs";

    public static ChangedFileClass Classify(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string[] segments = Unquote(path).Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return ChangedFileClass.Production;
        }

        string[] directories = segments[..^1];
        string fileName = segments[^1];

        if (directories.Any(IsTestDirectory) || IsTestFileName(fileName))
        {
            return ChangedFileClass.Test;
        }

        if ((directories.Length > 0 && string.Equals(directories[0], DocsDirectory, StringComparison.OrdinalIgnoreCase))
            || OtherExtensions.Contains(Path.GetExtension(fileName)))
        {
            return ChangedFileClass.Other;
        }

        return ChangedFileClass.Production;
    }

    private static bool IsTestDirectory(string directory) =>
        TestDirectories.Contains(directory)
        || TestProjectSuffixes.Any(suffix => directory.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static bool IsTestFileName(string fileName) =>
        HasInnerMarker(fileName, ".spec.")
        || HasInnerMarker(fileName, ".test.")
        || fileName.EndsWith("_test.go", StringComparison.Ordinal)
        || (fileName.StartsWith("test_", StringComparison.Ordinal) && fileName.EndsWith(".py", StringComparison.Ordinal))
        || fileName.EndsWith(".bats", StringComparison.Ordinal);

    // `*.spec.*`: the marker followed by at least one more character, so `foo.spec.` alone is not a test.
    private static bool HasInnerMarker(string fileName, string marker)
    {
        int index = fileName.IndexOf(marker, StringComparison.Ordinal);
        return index >= 0 && index + marker.Length < fileName.Length;
    }

    // git quotes a path holding unusual characters; the quotes are not part of any directory or file name.
    private static string Unquote(string path) =>
        path.Length >= 2 && path[0] == '"' && path[^1] == '"' ? path[1..^1] : path;
}
