namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>What a changed path is to a review (ADR 0114), as <see cref="ChangedFileClassifier"/> classes it.</summary>
public enum ChangedFileClass
{
    /// <summary>Code that ships: any path that is neither a test nor documentation or an image.</summary>
    Production,

    /// <summary>A test file, or a file in a test directory or test project.</summary>
    Test,

    /// <summary>Documentation or an image: neither production code nor a test.</summary>
    Other,
}
