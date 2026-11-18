using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.ValueObjects.WebhookPublicBaseUrlTests;

public sealed class Create
{
    [Fact]
    public void WhenHttpsUrlWithWhitespaceAndTrailingSlash_StoresTrimmedUrl()
    {
        // Act
        Result<WebhookPublicBaseUrl> result = WebhookPublicBaseUrl.Create(" https://hooks.example.com/foundry/ ");

        // Assert
        WebhookPublicBaseUrl url = result.ShouldBeOfType<Result<WebhookPublicBaseUrl>.Success>().Value;
        url.Value.ShouldBe("https://hooks.example.com/foundry");
    }

    [Fact]
    public void WhenHttpsUrlAtMaximumLength_Succeeds()
    {
        // Arrange
        string prefix = "https://hooks.example.com/";
        string value = prefix + new string('a', 2048 - prefix.Length);

        // Act
        Result<WebhookPublicBaseUrl> result = WebhookPublicBaseUrl.Create(value);

        // Assert
        WebhookPublicBaseUrl url = result.ShouldBeOfType<Result<WebhookPublicBaseUrl>.Success>().Value;
        url.Value.ShouldBe(value);
    }

    [Fact]
    public void WhenLongerThanMaximumLength_FailsWithInvalidUrlCode()
    {
        // Arrange
        string prefix = "https://hooks.example.com/";
        string value = prefix + new string('a', 2049 - prefix.Length);

        // Act
        Result<WebhookPublicBaseUrl> result = WebhookPublicBaseUrl.Create(value);

        // Assert
        Result<WebhookPublicBaseUrl>.Failure failure = result.ShouldBeOfType<Result<WebhookPublicBaseUrl>.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidWebhookPublicBaseUrl");
    }

    [Theory]
    [InlineData("http://hooks.example.com")]
    [InlineData("hooks.example.com")]
    [InlineData("/relative/path")]
    [InlineData("https://hooks.example.com/?token=1")]
    [InlineData("https://hooks.example.com/#fragment")]
    [InlineData("https://user:pass@hooks.example.com")]
    [InlineData("ftp://hooks.example.com")]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenUrlNotAbsoluteHttps_FailsWithInvalidUrlCode(string value)
    {
        // Act
        Result<WebhookPublicBaseUrl> result = WebhookPublicBaseUrl.Create(value);

        // Assert
        Result<WebhookPublicBaseUrl>.Failure failure = result.ShouldBeOfType<Result<WebhookPublicBaseUrl>.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidWebhookPublicBaseUrl");
    }
}
