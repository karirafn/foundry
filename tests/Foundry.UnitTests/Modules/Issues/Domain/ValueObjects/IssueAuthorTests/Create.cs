using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.IssueAuthorTests;

public sealed class Create
{
    [Fact]
    public void WhenUsernameIsValid_ReturnsSuccessWithValue()
    {
        // Arrange
        string username = "octocat";

        // Act
        Result<IssueAuthor>.Success result = IssueAuthor.Create(username)
            .ShouldBeOfType<Result<IssueAuthor>.Success>();

        // Assert
        result.Value.Value.ShouldBe(username);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void WhenUsernameIsNullOrWhiteSpace_ReturnsFailure(string? username)
    {
        // Arrange

        // Act
        Result<IssueAuthor> result = IssueAuthor.Create(username!);

        // Assert
        result.ShouldBeOfType<Result<IssueAuthor>.Failure>();
    }
}
