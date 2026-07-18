using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.NamespaceTests;

public sealed class PrefixesOf
{
    private static RepositorySlug Slug(string slug) =>
        RepositorySlug.Create(slug).ValueOrThrow();

    [Fact]
    public void WhenSlugHasSingleSegmentOwner_ReturnsSinglePrefix()
    {
        // Arrange
        RepositorySlug slug = Slug("efla/databridge");

        // Act
        IReadOnlyList<Namespace> prefixes = Namespace.PrefixesOf(slug);

        // Assert
        prefixes.Count.ShouldBe(1);
        prefixes[0].Value.ShouldBe("efla");
    }

    [Fact]
    public void WhenSlugHasTwoSegmentOwner_ReturnsTwoPrefixesLongestFirst()
    {
        // Arrange — slug "efla/databridge/myproject" has owner "efla/databridge"
        RepositorySlug slug = Slug("efla/databridge/myproject");

        // Act
        IReadOnlyList<Namespace> prefixes = Namespace.PrefixesOf(slug);

        // Assert
        prefixes.Count.ShouldBe(2);
        prefixes[0].Value.ShouldBe("efla/databridge");
        prefixes[1].Value.ShouldBe("efla");
    }

    [Fact]
    public void WhenSlugHasThreeSegmentOwner_ReturnsThreePrefixesLongestFirst()
    {
        // Arrange — slug "a/b/c/project" has owner "a/b/c"
        RepositorySlug slug = Slug("a/b/c/project");

        // Act
        IReadOnlyList<Namespace> prefixes = Namespace.PrefixesOf(slug);

        // Assert
        prefixes.Count.ShouldBe(3);
        prefixes[0].Value.ShouldBe("a/b/c");
        prefixes[1].Value.ShouldBe("a/b");
        prefixes[2].Value.ShouldBe("a");
    }

    [Fact]
    public void WhenSlugHasTwoSegmentOwner_PrefixSegmentCountsAreDescending()
    {
        // Arrange
        RepositorySlug slug = Slug("efla/databridge/akraborg.databridge");

        // Act
        IReadOnlyList<Namespace> prefixes = Namespace.PrefixesOf(slug);

        // Assert
        prefixes[0].SegmentCount.ShouldBe(2);
        prefixes[1].SegmentCount.ShouldBe(1);
    }
}
