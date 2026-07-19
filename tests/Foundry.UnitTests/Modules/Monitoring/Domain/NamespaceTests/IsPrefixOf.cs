using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.NamespaceTests;

public sealed class IsPrefixOf
{
    private static RepositorySlug Slug(string slug) =>
        RepositorySlug.Create(slug).ValueOrThrow();

    private static Namespace Ns(string value) =>
        Namespace.Create(value).ValueOrThrow();

    [Fact]
    public void WhenNamespaceMatchesOwnerExactly_ReturnsTrue()
    {
        // Arrange
        Namespace ns = Ns("efla");
        RepositorySlug slug = Slug("efla/databridge");

        // Act
        bool result = ns.IsPrefixOf(slug);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenNamespaceIsSubgroupPrefixOfOwner_ReturnsTrue()
    {
        // Arrange
        Namespace ns = Ns("efla");
        RepositorySlug slug = Slug("efla/databridge/akraborg.databridge");

        // Act
        bool result = ns.IsPrefixOf(slug);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenNamespaceMatchesFullMultiSegmentOwnerExactly_ReturnsTrue()
    {
        // Arrange
        Namespace ns = Ns("efla/databridge");
        RepositorySlug slug = Slug("efla/databridge/akraborg.databridge");

        // Act
        bool result = ns.IsPrefixOf(slug);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenNamespaceIsNotSegmentBoundaryMatch_ReturnsFalse()
    {
        // Arrange
        Namespace ns = Ns("efla");
        RepositorySlug slug = Slug("eflax/someproject");

        // Act
        bool result = ns.IsPrefixOf(slug);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenNamespaceDoesNotMatchOwner_ReturnsFalse()
    {
        // Arrange
        Namespace ns = Ns("acme");
        RepositorySlug slug = Slug("efla/databridge");

        // Act
        bool result = ns.IsPrefixOf(slug);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenNamespaceIsLongerThanOwner_ReturnsFalse()
    {
        // Arrange
        Namespace ns = Ns("efla/databridge/sub");
        RepositorySlug slug = Slug("efla/databridge");

        // Act
        bool result = ns.IsPrefixOf(slug);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenNamespaceMatchesTwoSegmentOwnerExactly_ReturnsTrue()
    {
        // Arrange — slug "efla/databridge/myproject" has owner "efla/databridge"
        Namespace ns = Ns("efla/databridge");
        RepositorySlug slug = Slug("efla/databridge/myproject");

        // Act
        bool result = ns.IsPrefixOf(slug);

        // Assert
        result.ShouldBeTrue();
    }
}
