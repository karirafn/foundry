using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ProviderUrlTests;

public sealed class Create
{
    [Fact]
    public void WhenUriIsValidAndAbsolute_ReturnsSuccessWithValue()
    {
        // Arrange
        string url = "https://github.com/owner/repo/issues/42";

        // Act
        Result<ProviderUrl>.Success result = ProviderUrl.Create(url)
            .ShouldBeOfType<Result<ProviderUrl>.Success>();

        // Assert
        result.Value.Value.ShouldBe(new Uri(url));
    }

    [Fact]
    public void WhenUriUsesHttpScheme_ReturnsSuccess()
    {
        // Arrange
        string url = "http://github.com/owner/repo/issues/42";

        // Act
        Result<ProviderUrl>.Success result = ProviderUrl.Create(url)
            .ShouldBeOfType<Result<ProviderUrl>.Success>();

        // Assert
        result.Value.Value.ShouldBe(new Uri(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    public void WhenUriIsInvalidOrRelative_ReturnsFailure(string? url)
    {
        // Arrange

        // Act
        Result<ProviderUrl> result = ProviderUrl.Create(url!);

        // Assert
        result.ShouldBeOfType<Result<ProviderUrl>.Failure>();
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://ftp.example.com/file")]
    [InlineData("javascript:alert(1)")]
    public void WhenUriUsesDisallowedScheme_ReturnsFailure(string url)
    {
        // Arrange

        // Act
        Result<ProviderUrl> result = ProviderUrl.Create(url);

        // Assert
        result.ShouldBeOfType<Result<ProviderUrl>.Failure>();
    }
}
