using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.AccountNameTests;

public sealed class FromPersistedString
{
    [Fact]
    public void WhenStoredNameFailsTheCreateRule_StillLoadsIt()
    {
        // Arrange — a legacy hand-entered name from before names were derived from the token (#287).
        string legacy = "work gh\taccount";

        // Act
        AccountName name = AccountName.FromPersistedString(legacy);

        // Assert
        name.Value.ShouldBe(legacy);
    }
}
