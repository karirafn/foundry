namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>What a changed path holds, as a reviewer is judged against it (ADR 0114).</summary>
public enum ChangedFileClass
{
    Production,
    Test,
    Other,
}

/// <summary>
/// Classes a changed path by fixed rules: a path in a test directory or named like a test is <see cref="ChangedFileClass.Test"/>,
/// documentation and images are <see cref="ChangedFileClass.Other"/>, and everything else is
/// <see cref="ChangedFileClass.Production"/>. The test rules win, so a README in a test project is a test path.
/// </summary>
public static class ChangedFileClassifier
{
    private static readonly string[] TestDirectoryNames = ["test", "tests", "__tests__", "spec"];

    private static readonly string[] TestDirectorySuffixes = [".Tests", ".UnitTests", ".IntegrationTests"];

    private static readonly string[] OtherExtensions = [".md", ".txt", ".png", ".jpg", ".gif", ".svg"];

    private const string DocsDirectory = "docs";

    public static ChangedFileClass Classify(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // git quotes a path holding unusual characters; the rules read the characters inside the quotes.
        string unquoted = path.Length >= 2 && path[0] == '"' && path[^1] == '"' ? path[1..^1] : path;
        string[] segments = unquoted.Split('/');
        string[] directories = segments[..^1];
        string fileName = segments[^1];

        if (directories.Any(IsTestDirectory) || IsTestFileName(fileName))
        {
            return ChangedFileClass.Test;
        }

        if ((directories.Length > 0 && string.Equals(directories[0], DocsDirectory, StringComparison.Ordinal))
            || OtherExtensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            return ChangedFileClass.Other;
        }

        return ChangedFileClass.Production;
    }

    private static bool IsTestDirectory(string directory) =>
        TestDirectoryNames.Any(name => string.Equals(directory, name, StringComparison.OrdinalIgnoreCase))
        || TestDirectorySuffixes.Any(suffix => directory.EndsWith(suffix, StringComparison.Ordinal));

    private static bool IsTestFileName(string fileName) =>
        HasInnerPart(fileName, ".spec.")
        || HasInnerPart(fileName, ".test.")
        || fileName.EndsWith("_test.go", StringComparison.Ordinal)
        || (fileName.StartsWith("test_", StringComparison.Ordinal) && fileName.EndsWith(".py", StringComparison.Ordinal))
        || fileName.EndsWith(".bats", StringComparison.Ordinal);

    // `*.spec.*`: the part needs a name before it and an extension after it.
    private static bool HasInnerPart(string fileName, string part)
    {
        int index = fileName.IndexOf(part, StringComparison.Ordinal);
        return index > 0 && index + part.Length < fileName.Length;
    }
}
