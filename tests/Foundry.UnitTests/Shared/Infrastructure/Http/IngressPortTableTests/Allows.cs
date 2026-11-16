using Foundry.Shared.Infrastructure.Http.Ingress;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Http.IngressPortTableTests;

public sealed class Allows
{
    private const int MainPort = 5000;
    private const int WebhookPort = 5099;
    private const int ProxyPort = 5199;

    private static readonly IngressPortTable TwoEntries = new(
    [
        new IngressEntry(WebhookPort, "/webhooks"),
        new IngressEntry(ProxyPort, "/package-sources"),
    ]);

    [Theory]
    [InlineData(WebhookPort, "/webhooks/github/abc")]
    [InlineData(ProxyPort, "/package-sources/1/index.json")]
    public void WhenPathMatchesItsOwnPortsPrefix_IsTrue(int localPort, string path)
    {
        // Act
        bool allowed = TwoEntries.Allows(localPort, path);

        // Assert
        allowed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ProxyPort, "/webhooks/github/abc")]
    [InlineData(MainPort, "/webhooks/github/abc")]
    [InlineData(WebhookPort, "/package-sources/1/index.json")]
    [InlineData(MainPort, "/package-sources/1/index.json")]
    public void WhenPrefixedPathArrivesOnAnotherPort_IsFalse(int localPort, string path)
    {
        // Act
        bool allowed = TwoEntries.Allows(localPort, path);

        // Assert
        allowed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(WebhookPort, "/api/accounts")]
    [InlineData(ProxyPort, "/api/accounts")]
    [InlineData(WebhookPort, "/")]
    [InlineData(ProxyPort, "/webhooksx")]
    public void WhenUnprefixedPathArrivesOnAnIngressPort_IsFalse(int localPort, string path)
    {
        // Act
        bool allowed = TwoEntries.Allows(localPort, path);

        // Assert
        allowed.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/api/accounts")]
    [InlineData("/")]
    [InlineData("/webhooksx")]
    public void WhenUnprefixedPathArrivesOnTheMainPort_IsTrue(string path)
    {
        // Act
        bool allowed = TwoEntries.Allows(MainPort, path);

        // Assert
        allowed.ShouldBeTrue();
    }

    [Fact]
    public void WhenPrefixDiffersOnlyInCase_MatchesTheEntry()
    {
        // Act
        bool onMainPort = TwoEntries.Allows(MainPort, "/WebHooks/github/abc");
        bool onWebhookPort = TwoEntries.Allows(WebhookPort, "/WebHooks/github/abc");

        // Assert
        onMainPort.ShouldBeFalse();
        onWebhookPort.ShouldBeTrue();
    }

    [Theory]
    [InlineData(MainPort, "/webhooks/github/abc")]
    [InlineData(WebhookPort, "/api/accounts")]
    public void WhenNoEntries_EveryRequestIsAllowed(int localPort, string path)
    {
        // Arrange
        IngressPortTable table = new([]);

        // Act
        bool allowed = table.Allows(localPort, path);

        // Assert
        allowed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(MainPort)]
    [InlineData(WebhookPort)]
    public void WhenEntrysPortIsUnset_ItsPrefixIsServedOnNoPort(int localPort)
    {
        // Arrange
        IngressPortTable table = new([new IngressEntry(null, "/webhooks")]);

        // Act
        bool prefixed = table.Allows(localPort, "/webhooks/github/abc");
        bool unprefixed = table.Allows(localPort, "/api/accounts");

        // Assert
        prefixed.ShouldBeFalse();
        unprefixed.ShouldBeTrue();
    }
}
