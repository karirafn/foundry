using Foundry.Shared.Infrastructure.Http.Ingress;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Http.IngressPortTableTests;

public sealed class Construction
{
    [Fact]
    public void WhenTwoEntriesShareAPort_ThrowsNamingThePort()
    {
        // Act
        InvalidOperationException exception = Should.Throw<InvalidOperationException>(() => new IngressPortTable(
        [
            new IngressEntry(5099, "/webhooks"),
            new IngressEntry(5099, "/package-sources"),
        ]));

        // Assert
        exception.Message.ShouldContain("5099");
    }

    [Theory]
    [InlineData(5099, true)]
    [InlineData(5199, true)]
    [InlineData(5000, false)]
    public void IsIngressPort_IsTrueOnlyForAnEntrysPort(int localPort, bool expected)
    {
        // Arrange
        IngressPortTable table = new([new IngressEntry(5099, "/webhooks"), new IngressEntry(5199, "/package-sources")]);

        // Act
        bool isIngressPort = table.IsIngressPort(localPort);

        // Assert
        isIngressPort.ShouldBe(expected);
    }

    [Fact]
    public void WhenTwoEntriesHaveNoPort_DoesNotThrow()
    {
        // Act
        IngressPortTable table = new([new IngressEntry(null, "/webhooks"), new IngressEntry(null, "/package-sources")]);

        // Assert
        table.IsIngressPort(5000).ShouldBeFalse();
    }
}
