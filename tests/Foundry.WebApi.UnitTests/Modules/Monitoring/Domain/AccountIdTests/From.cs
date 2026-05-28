using Foundry.WebApi.Modules.Monitoring.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Domain.AccountIdTests;

public sealed class From
{
    [Fact]
    public void WhenGivenGuid_ReturnsIdWithSameValue()
    {
        // Arrange
        Guid guid = Guid.NewGuid();

        // Act
        AccountId id = AccountId.From(guid);

        // Assert
        id.Value.ShouldBe(guid);
    }
}
