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

    [Theory]
    [InlineData("https://localhost")]
    [InlineData("https://LocalHost:8443/foundry")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://127.10.20.30")]
    [InlineData("https://[::1]")]
    [InlineData("https://169.254.10.1")]
    [InlineData("https://[fe80::1]")]
    [InlineData("https://[febf::1]")]
    [InlineData("https://0.0.0.0")]
    [InlineData("https://[::]")]
    [InlineData("https://[::ffff:127.0.0.1]")]
    public void WhenHostUndeliverable_FailsWithHostDetail(string value)
    {
        // Act
        Result<WebhookPublicBaseUrl> result = WebhookPublicBaseUrl.Create(value);

        // Assert
        Result<WebhookPublicBaseUrl>.Failure failure = result.ShouldBeOfType<Result<WebhookPublicBaseUrl>.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidWebhookPublicBaseUrl");
        failure.Error.Message.ShouldStartWith("GitHub and GitLab can't deliver to this address.");
    }

    [Theory]
    [InlineData("https://10.0.0.5")]
    [InlineData("https://172.16.4.2")]
    [InlineData("https://192.168.1.10")]
    [InlineData("https://[fd12::1]")]
    [InlineData("https://hooks.example.com")]
    [InlineData("https://laptop.tailnet.ts.net")]
    [InlineData("https://localhost.example.com")]
    public void WhenHostPrivateOrDnsName_Succeeds(string value)
    {
        // Act
        Result<WebhookPublicBaseUrl> result = WebhookPublicBaseUrl.Create(value);

        // Assert
        result.ShouldBeOfType<Result<WebhookPublicBaseUrl>.Success>().Value.Value.ShouldBe(value);
    }

    [Fact]
    public void WhenShapeInvalid_KeepsShapeDetail()
    {
        // Act
        Result<WebhookPublicBaseUrl> result = WebhookPublicBaseUrl.Create("http://localhost");

        // Assert
        Result<WebhookPublicBaseUrl>.Failure failure = result.ShouldBeOfType<Result<WebhookPublicBaseUrl>.Failure>();
        failure.Error.Message.ShouldStartWith("The webhook public base URL must be an absolute https URL");
    }
}
