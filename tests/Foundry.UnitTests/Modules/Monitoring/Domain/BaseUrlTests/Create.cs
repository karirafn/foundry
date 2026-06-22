using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.BaseUrlTests;

public sealed class Create
{
    [Fact]
    public void WhenUrlIsValidHttps_ReturnsBaseUrl()
    {
        // Arrange
        string url = "https://github.com";

        // Act
        Result<BaseUrl> result = BaseUrl.Create(url);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        BaseUrl baseUrl = ((Result<BaseUrl>.Success)result).Value;
        baseUrl.Value.ShouldBe(new Uri(url));
    }

    [Theory]
    [InlineData("http://github.com")]
    [InlineData("ftp://github.com")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void WhenUrlIsNotValidHttps_ReturnsInvalidError(string url)
    {
        // Act
        Result<BaseUrl> result = BaseUrl.Create(url);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result<BaseUrl>.Failure)result).Error;
        error.ShouldBe(BaseUrlErrors.Invalid);
    }

    [Fact]
    public void WhenUrlContainsNonEmptyUserInfo_ReturnsContainsCredentialsError()
    {
        // Arrange
        string url = "https://attacker@github.com";

        // Act
        Result<BaseUrl> result = BaseUrl.Create(url);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result<BaseUrl>.Failure)result).Error;
        error.ShouldBe(BaseUrlErrors.ContainsCredentials);
    }

    [Fact]
    public void WhenUrlHasEmptyUserInfo_ReturnsContainsCredentialsError()
    {
        // Arrange
        string url = "https://@gitlab.com";

        // Act
        Result<BaseUrl> result = BaseUrl.Create(url);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result<BaseUrl>.Failure)result).Error;
        error.ShouldBe(BaseUrlErrors.ContainsCredentials);
    }

    [Fact]
    public void WhenUrlIsValidSelfHosted_ReturnsBaseUrl()
    {
        // Arrange
        string url = "https://gitlab.example.com";

        // Act
        Result<BaseUrl> result = BaseUrl.Create(url);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        BaseUrl baseUrl = ((Result<BaseUrl>.Success)result).Value;
        baseUrl.Value.ShouldBe(new Uri(url));
    }

    [Fact]
    public void WhenFromPersistedStringCalledWithValidUrl_ReturnsBaseUrl()
    {
        // Arrange
        string url = "https://github.com";

        // Act
        BaseUrl result = BaseUrl.FromPersistedString(url);

        // Assert
        result.Value.ShouldBe(new Uri(url));
    }

    [Fact]
    public void WhenFromPersistedStringCalledWithInvalidUrl_ThrowsInvalidOperationException()
    {
        // Arrange
        string url = "http://not-https.com";

        // Act
        Action act = () => BaseUrl.FromPersistedString(url);

        // Assert
        Should.Throw<InvalidOperationException>(act);
    }
}
