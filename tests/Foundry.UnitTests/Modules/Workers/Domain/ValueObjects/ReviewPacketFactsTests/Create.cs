using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ReviewPacketFactsTests;

public sealed class Create
{
    private static readonly PatchId Id = PatchId.From("0123456789abcdef0123456789abcdef01234567");

    [Fact]
    public void WhenAPatchIdComesWithNoChangedPath_Throws()
    {
        // Act / Assert
        Should.Throw<ArgumentException>(() => new ReviewPacketFacts([], Id));
    }

    [Fact]
    public void WhenTheCallerChangesItsListAfterward_ThePacketKeepsItsPaths()
    {
        // Arrange
        List<ChangedPath> paths = [new ChangedPath(ChangeStatus.Modified, "src/a.cs")];
        ReviewPacketFacts facts = new(paths, Id);

        // Act
        paths.Add(new ChangedPath(ChangeStatus.Added, "src/b.cs"));

        // Assert
        facts.ChangedPaths.ShouldBe([new ChangedPath(ChangeStatus.Modified, "src/a.cs")]);
    }

    [Fact]
    public void TwoPacketsWithTheSamePathsAndPatchId_AreEqual()
    {
        // Act
        ReviewPacketFacts first = new([new ChangedPath(ChangeStatus.Deleted, "a b.txt")], Id);
        ReviewPacketFacts second = new([new ChangedPath(ChangeStatus.Deleted, "a b.txt")], Id);

        // Assert
        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }
}
