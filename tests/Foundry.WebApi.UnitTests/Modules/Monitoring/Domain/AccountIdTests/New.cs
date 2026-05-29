using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Domain.AccountIdTests;

public sealed class New
{
    [Fact]
    public void WhenCalledTwice_ProducesDistinctIds()
    {
        // Arrange

        // Act
        AccountId a = AccountId.New();
        AccountId b = AccountId.New();

        // Assert
        a.ShouldNotBe(b);
    }
}
