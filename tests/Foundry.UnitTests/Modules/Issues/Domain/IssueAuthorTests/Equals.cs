using Foundry.Modules.Issues.Domain;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.IssueAuthorTests;

public sealed class Equals
{
    [Fact]
    public void WhenSameUsername_AuthorsAreEqual()
    {
        // Arrange
        IssueAuthor a = IssueAuthor.Create("octocat").ValueOrThrow();
        IssueAuthor b = IssueAuthor.Create("octocat").ValueOrThrow();

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenDifferentUsername_AuthorsAreNotEqual()
    {
        // Arrange
        IssueAuthor a = IssueAuthor.Create("octocat").ValueOrThrow();
        IssueAuthor b = IssueAuthor.Create("hubot").ValueOrThrow();

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeFalse();
    }
}
