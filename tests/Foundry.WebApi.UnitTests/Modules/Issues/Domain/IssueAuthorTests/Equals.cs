using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Domain.IssueAuthorTests;

public sealed class Equals
{
    [Fact]
    public void WhenSameUsername_AuthorsAreEqual()
    {
        // Arrange
        IssueAuthor a = ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;
        IssueAuthor b = ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenDifferentUsername_AuthorsAreNotEqual()
    {
        // Arrange
        IssueAuthor a = ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;
        IssueAuthor b = ((Result<IssueAuthor>.Success)IssueAuthor.Create("hubot")).Value;

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeFalse();
    }
}
