using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialIdTests;

public sealed class New
{
    [Fact]
    public void WhenCalledTwice_ProducesDistinctIds()
    {
        // Arrange

        // Act
        CredentialId a = CredentialId.New();
        CredentialId b = CredentialId.New();

        // Assert
        a.ShouldNotBe(b);
    }
}
