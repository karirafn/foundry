using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features;

/// <summary>
/// Guards that each conflict-reason enum contains exactly the members the endpoint produces.
/// A failure here means someone added a reason value without wiring a producing arm,
/// or removed a producing arm without updating the enum.
/// </summary>
public sealed class AccountConflictContractTests
{
    [Fact]
    public void CreateAccountConflictReason_HasExactlyDuplicateAccountAndNamespaceConflict()
    {
        // Arrange
        CreateAccountConflictReason[] expected =
        [
            CreateAccountConflictReason.DuplicateAccount,
            CreateAccountConflictReason.NamespaceConflict,
        ];

        // Act
        CreateAccountConflictReason[] actual = Enum.GetValues<CreateAccountConflictReason>();

        // Assert
        actual.ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void UpdateAccountConflictReason_HasExactlyClaimedElsewhereAndDuplicateNamespaceAndDuplicateAccount()
    {
        // Arrange
        UpdateAccountConflictReason[] expected =
        [
            UpdateAccountConflictReason.ClaimedElsewhere,
            UpdateAccountConflictReason.DuplicateNamespace,
            UpdateAccountConflictReason.DuplicateAccount,
        ];

        // Act
        UpdateAccountConflictReason[] actual = Enum.GetValues<UpdateAccountConflictReason>();

        // Assert
        actual.ShouldBe(expected, ignoreOrder: true);
    }
}
