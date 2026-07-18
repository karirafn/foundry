using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.AccountIdTests;

public sealed class From
{
    [Fact]
    public void WhenGivenGuid_ReturnsCredentialIdWithSameValue()
    {
        // Arrange
        Guid guid = Guid.NewGuid();

        // Act
        CredentialId id = CredentialId.From(guid);

        // Assert
        id.Value.ShouldBe(guid);
    }
}
