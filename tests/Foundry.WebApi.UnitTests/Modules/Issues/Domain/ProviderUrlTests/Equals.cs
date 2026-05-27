using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Domain.ProviderUrlTests;

public sealed class Equals
{
    [Fact]
    public void WhenSameUrl_ProviderUrlsAreEqual()
    {
        // Arrange
        string url = "https://github.com/owner/repo/issues/1";
        ProviderUrl a = ((Result<ProviderUrl>.Success)ProviderUrl.Create(url)).Value;
        ProviderUrl b = ((Result<ProviderUrl>.Success)ProviderUrl.Create(url)).Value;

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenDifferentUrl_ProviderUrlsAreNotEqual()
    {
        // Arrange
        ProviderUrl a = ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;
        ProviderUrl b = ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/2")).Value;

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeFalse();
    }
}
