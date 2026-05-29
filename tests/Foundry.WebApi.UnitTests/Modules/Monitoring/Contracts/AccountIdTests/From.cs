using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Contracts.AccountIdTests;

public sealed class From
{
    [Fact]
    public void WhenGivenGuid_ReturnsAccountIdWithSameValue()
    {
        // Arrange
        Guid guid = Guid.NewGuid();

        // Act
        AccountId id = AccountId.From(guid);

        // Assert
        id.Value.ShouldBe(guid);
    }
}
