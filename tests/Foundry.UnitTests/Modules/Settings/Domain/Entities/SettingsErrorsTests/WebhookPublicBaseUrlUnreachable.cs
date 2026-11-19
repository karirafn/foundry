using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.SettingsErrorsTests;

public sealed class WebhookPublicBaseUrlUnreachable
{
    private static WebhookPublicBaseUrl Url(string value) =>
        WebhookPublicBaseUrl.Create(value).ShouldBeOfType<Result<WebhookPublicBaseUrl>.Success>().Value;

    private static Error Build(WebhookProbeFailure failure, string url = "https://hooks.example.com") =>
        SettingsErrors.WebhookPublicBaseUrlUnreachable(failure, Url(url), 5099);

    [Fact]
    public void Code_IsFieldKeyedUnreachable()
    {
        // Act
        Error error = Build(new WebhookProbeFailure.Timeout());

        // Assert
        error.Code.ShouldBe("Settings.WebhookPublicBaseUrlUnreachable");
    }

    [Fact]
    public void WhenDns_NamesTheHost()
    {
        // Act
        Error error = Build(new WebhookProbeFailure.Dns());

        // Assert
        error.Message.ShouldBe("Couldn't resolve hooks.example.com. Check the URL for typos and that its DNS record or tunnel hostname exists. A quick tunnel gets a new address each time it starts — copy the current one.");
    }

    [Fact]
    public void WhenTls_NamesTheHost()
    {
        // Act
        Error error = Build(new WebhookProbeFailure.Tls());

        // Assert
        error.Message.ShouldBe("hooks.example.com answered, but its HTTPS certificate was rejected. Make sure the tunnel or proxy serves a valid, unexpired certificate for hooks.example.com from a public certificate authority.");
    }

    [Fact]
    public void WhenTimeout_NamesHostAndPort()
    {
        // Act
        Error error = Build(new WebhookProbeFailure.Timeout());

        // Assert
        error.Message.ShouldBe("hooks.example.com didn't answer within 10 seconds. Check that the tunnel or proxy is running and forwards to port 5099 on the Foundry host. If the proxy sits behind the same router as Foundry, the router may not route requests back to its own public address.");
    }

    [Fact]
    public void WhenOtherInstance_PointsAtTheWebhookPort()
    {
        // Act
        Error error = Build(new WebhookProbeFailure.OtherInstance());

        // Assert
        error.Message.ShouldBe("A server at hooks.example.com answered, but it isn't this Foundry. Point the tunnel or proxy at port 5099 — the webhook port — not Foundry's main port or another service.");
    }

    [Theory]
    [InlineData("https://hooks.example.com/webhooks")]
    [InlineData("https://hooks.example.com/foundry/WebHooks/")]
    public void WhenOtherInstanceAndUrlEndsWithWebhooks_AddsTheRemoveHint(string url)
    {
        // Act
        Error error = Build(new WebhookProbeFailure.OtherInstance(), url);

        // Assert
        error.Message.ShouldEndWith("another service. Remove /webhooks from the end of the URL — Foundry adds it.");
    }

    [Fact]
    public void WhenHttpStatus_NamesTheStatus()
    {
        // Act
        Error error = Build(new WebhookProbeFailure.HttpStatus(403));

        // Assert
        error.Message.ShouldBe("hooks.example.com answered with HTTP 403 instead of passing the request to Foundry. Check for an access policy, login page, redirect or firewall rule in front of /webhooks/*.");
    }
}
