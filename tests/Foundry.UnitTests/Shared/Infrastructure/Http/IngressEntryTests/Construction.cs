using Foundry.Shared.Infrastructure.Http.Ingress;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Http.IngressEntryTests;

public sealed class Construction
{
    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void WhenPortIsOutOfRange_Throws(int port)
    {
        // Act
        Action act = () => _ = new IngressEntry(port, "/webhooks");

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public void WhenPathPrefixIsEmptyOrRoot_Throws(string pathPrefix)
    {
        // Act
        Action act = () => _ = new IngressEntry(5099, pathPrefix);

        // Assert
        Should.Throw<ArgumentException>(act);
    }
}
