using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.ProviderUrlTests;

public sealed class Equals
{
    [Fact]
    public void WhenSameUrl_ProviderUrlsAreEqual()
    {
        // Arrange
        string url = "https://github.com/owner/repo/issues/1";
        ProviderUrl a = ProviderUrl.Create(url).ValueOrThrow();
        ProviderUrl b = ProviderUrl.Create(url).ValueOrThrow();

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenDifferentUrl_ProviderUrlsAreNotEqual()
    {
        // Arrange
        ProviderUrl a = ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();
        ProviderUrl b = ProviderUrl.Create("https://github.com/owner/repo/issues/2").ValueOrThrow();

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeFalse();
    }
}
