using Foundry.WebApi;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.IngressKestrelBindingTests;

public sealed class AddIngressEndpoints
{
    private const string WebhookPortKey = "Webhooks:Port";
    private const string ProxyPortKey = "PackageSources:ProxyPort";

    [Fact]
    public void WhenTwoPortsAreConfigured_AppendsBothToTheConfiguredUrls()
    {
        // Arrange
        WebApplicationBuilder builder = CreateBuilder(
            ("urls", "http://localhost:5000"),
            (WebhookPortKey, "5099"),
            (ProxyPortKey, "5199"));

        // Act
        IngressKestrelBinding.AddIngressEndpoints(builder, WebhookPortKey, ProxyPortKey);

        // Assert
        builder.WebHost.GetSetting(WebHostDefaults.ServerUrlsKey)
            .ShouldBe("http://localhost:5000;http://*:5099;http://*:5199");
    }

    [Fact]
    public void WhenNoPortIsConfigured_LeavesTheUrlsUntouched()
    {
        // Arrange
        WebApplicationBuilder builder = CreateBuilder(("urls", "http://localhost:5000"));

        // Act
        IngressKestrelBinding.AddIngressEndpoints(builder, WebhookPortKey, ProxyPortKey);

        // Assert
        builder.WebHost.GetSetting(WebHostDefaults.ServerUrlsKey).ShouldBe("http://localhost:5000");
    }

    [Fact]
    public void WhenTwoKeysShareAPort_ThrowsNamingThePort()
    {
        // Arrange
        WebApplicationBuilder builder = CreateBuilder((WebhookPortKey, "5099"), (ProxyPortKey, "5099"));

        // Act
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(
            () => IngressKestrelBinding.AddIngressEndpoints(builder, WebhookPortKey, ProxyPortKey));

        // Assert
        exception.Message.ShouldContain("5099");
    }

    [Fact]
    public void WhenAPortWithADefaultIsUnset_BindsTheDefault()
    {
        // Arrange
        WebApplicationBuilder builder = CreateBuilder(("urls", "http://localhost:5000"));

        // Act
        IngressKestrelBinding.AddIngressEndpoints(
            builder,
            new IngressPortSetting(WebhookPortKey),
            new IngressPortSetting(ProxyPortKey, DefaultPort: 5287));

        // Assert
        builder.WebHost.GetSetting(WebHostDefaults.ServerUrlsKey).ShouldBe("http://localhost:5000;http://*:5287");
    }

    [Fact]
    public void WhenAPortWithADefaultIsConfigured_BindsTheConfiguredPort()
    {
        // Arrange
        WebApplicationBuilder builder = CreateBuilder(("urls", "http://localhost:5000"), (ProxyPortKey, "5199"));

        // Act
        IngressKestrelBinding.AddIngressEndpoints(builder, new IngressPortSetting(ProxyPortKey, DefaultPort: 5287));

        // Assert
        builder.WebHost.GetSetting(WebHostDefaults.ServerUrlsKey).ShouldBe("http://localhost:5000;http://*:5199");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("webhooks")]
    public void WhenPortIsNotATcpPort_ThrowsNamingTheKey(string value)
    {
        // Arrange
        WebApplicationBuilder builder = CreateBuilder((WebhookPortKey, value));

        // Act
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(
            () => IngressKestrelBinding.AddIngressEndpoints(builder, WebhookPortKey));

        // Assert
        exception.Message.ShouldContain(WebhookPortKey);
    }

    private static WebApplicationBuilder CreateBuilder(params (string Key, string Value)[] settings)
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        foreach ((string key, string value) in settings)
        {
            builder.Configuration[key] = value;
            if (key == "urls")
            {
                builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, value);
            }
        }

        return builder;
    }
}
