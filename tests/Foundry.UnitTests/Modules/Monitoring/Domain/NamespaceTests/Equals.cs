using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.NamespaceTests;

public sealed class Equals
{
    private static Namespace Ns(string value) =>
        Namespace.Create(value).ValueOrThrow();

    [Fact]
    public void WhenTwoNamespacesHaveSameValue_AreEqual()
    {
        // Arrange
        Namespace a = Ns("efla");
        Namespace b = Ns("efla");

        // Act & Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenTwoNamespacesHaveDifferentValues_AreNotEqual()
    {
        // Arrange
        Namespace a = Ns("efla");
        Namespace b = Ns("acme");

        // Act & Assert
        a.ShouldNotBe(b);
    }
}
