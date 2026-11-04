using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateWebhookSettings
{
    [Fact]
    public void WhenCreated_HasNoPublicBaseUrlAndDefaultBackstop()
    {
        // Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.WebhookPublicBaseUrl.ShouldBeNull();
        settings.BackstopIntervalMinutes.ShouldBe(15);
    }

    [Fact]
    public void WhenHttpsUrlWithTrailingSlash_StoresTrimmedUrl()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateWebhookSettings(" https://hooks.example.com/foundry/ ", 20);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        settings.WebhookPublicBaseUrl.ShouldBe("https://hooks.example.com/foundry");
        settings.BackstopIntervalMinutes.ShouldBe(20);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenUrlBlank_ClearsUrl(string? url)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdateWebhookSettings("https://hooks.example.com", 15);

        // Act
        Result result = settings.UpdateWebhookSettings(url, 15);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        settings.WebhookPublicBaseUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData("http://hooks.example.com")]
    [InlineData("hooks.example.com")]
    [InlineData("/relative/path")]
    [InlineData("https://hooks.example.com/?token=1")]
    [InlineData("https://hooks.example.com/#fragment")]
    [InlineData("https://user:pass@hooks.example.com")]
    [InlineData("ftp://hooks.example.com")]
    public void WhenUrlNotAbsoluteHttps_ReturnsFieldKeyedError(string url)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateWebhookSettings(url, 15);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidWebhookPublicBaseUrl");
        settings.WebhookPublicBaseUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    public void WhenBackstopOutOfBounds_ReturnsFieldKeyedError(int minutes)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateWebhookSettings(null, minutes);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidBackstopInterval");
        settings.BackstopIntervalMinutes.ShouldBe(15);
    }
}
