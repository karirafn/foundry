using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabErrorsTests;

public sealed class TryGetStatusCode
{
    [Theory]
    [InlineData(503)]
    [InlineData(408)]
    [InlineData(404)]
    public void WhenTheErrorIsAnUnexpectedStatusCode_ReturnsThatStatusCode(int statusCode)
    {
        // Arrange
        Error error = GitLabErrors.UnexpectedStatusCode(statusCode);

        // Act
        bool parsed = GitLabErrors.TryGetStatusCode(error, out int actual);

        // Assert
        parsed.ShouldBeTrue();
        actual.ShouldBe(statusCode);
    }

    [Fact]
    public void WhenTheErrorIsAnotherGitLabError_ReturnsFalse()
    {
        // Arrange
        Error error = GitLabErrors.RateLimitExhausted;

        // Act
        bool parsed = GitLabErrors.TryGetStatusCode(error, out int actual);

        // Assert
        parsed.ShouldBeFalse();
        actual.ShouldBe(0);
    }

    [Fact]
    public void WhenTheMessageDoesNotCarryTheExpectedPrefix_ReturnsFalse()
    {
        // Arrange
        Error error = new("GitLab.UnexpectedStatusCode", "something else entirely 503.");

        // Act
        bool parsed = GitLabErrors.TryGetStatusCode(error, out int _);

        // Assert
        parsed.ShouldBeFalse();
    }
}
