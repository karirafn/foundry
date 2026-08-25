using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

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
        BaseUrl baseUrl = result.ValueOrThrow();
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
        BaseUrl baseUrl = result.ValueOrThrow();
        baseUrl.Value.ShouldBe(new Uri(url));
    }

    [Fact]
    public void WhenUrlContainsQueryString_ReturnsInvalidError()
    {
        // Arrange
        string url = "https://gitlab.example.com/?x=1";

        // Act
        Result<BaseUrl> result = BaseUrl.Create(url);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result<BaseUrl>.Failure)result).Error;
        error.Code.ShouldBe("BaseUrl.Invalid");
    }

    [Fact]
    public void WhenUrlContainsFragment_ReturnsInvalidError()
    {
        // Arrange
        string url = "https://gitlab.example.com/#section";

        // Act
        Result<BaseUrl> result = BaseUrl.Create(url);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result<BaseUrl>.Failure)result).Error;
        error.Code.ShouldBe("BaseUrl.Invalid");
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

    [Fact]
    public void WhenHostIsLoopbackIpv4_ReturnsPrivateHostError()
    {
        // Arrange
        string url = "https://127.0.0.1";

        // Act
        Result<BaseUrl> result = BaseUrl.Create(url);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result<BaseUrl>.Failure)result).Error;
        error.ShouldBe(BaseUrlErrors.PrivateHost);
    }

    [Theory]
    [InlineData("https://[::1]")]                // IPv6 loopback
    [InlineData("https://169.254.169.254")]      // link-local (IMDS)
    [InlineData("https://10.0.0.1")]             // RFC-1918 class A
    [InlineData("https://192.168.1.1")]          // RFC-1918 class C
    [InlineData("https://172.16.0.1")]           // RFC-1918 class B
    [InlineData("https://140.82.121.4")]         // public IP — providers are DNS-named, so rejected
    public void WhenHostIsLiteralIpAddress_ReturnsPrivateHostError(string url)
    {
        // Act
        Result<BaseUrl> result = BaseUrl.Create(url);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Error error = ((Result<BaseUrl>.Failure)result).Error;
        error.ShouldBe(BaseUrlErrors.PrivateHost);
    }

    [Theory]
    [InlineData("https://github.com")]
    [InlineData("https://gitlab.com")]
    [InlineData("https://gitlab.example.com")]
    public void WhenHostIsDnsName_Succeeds(string url)
    {
        // Act
        Result<BaseUrl> result = BaseUrl.Create(url);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
