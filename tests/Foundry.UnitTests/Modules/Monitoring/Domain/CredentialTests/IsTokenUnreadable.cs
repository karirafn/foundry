using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class IsTokenUnreadable
{
    private static BaseUrl GitHubBaseUrl() => BaseUrl.Create("https://github.com").ValueOrThrow();

    [Fact]
    public void WhenTokenIsUnreadable_ReturnsTrue()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.CreateWithUnreadableToken("my-org", GitHubBaseUrl());

        // Act
        bool result = credential.IsTokenUnreadable;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenTokenIsPresent_ReturnsFalse()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_abc", GitHubBaseUrl());

        // Act
        bool result = credential.IsTokenUnreadable;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenTokenIsNull_ReturnsFalse()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create("my-org", null, GitHubBaseUrl());

        // Act
        bool result = credential.IsTokenUnreadable;

        // Assert
        result.ShouldBeFalse();
    }
}
