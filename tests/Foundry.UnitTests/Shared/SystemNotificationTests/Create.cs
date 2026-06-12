using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.SystemNotificationTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_HoldsProvidedValues()
    {
        // Arrange
        const string Category = "auth";
        const bool IsActive = true;
        const string Message = "Claude authentication is invalid.";

        // Act
        SystemNotification notification = new(Category, IsActive, Message);

        // Assert
        notification.ShouldSatisfyAllConditions(
            () => notification.Category.ShouldBe(Category),
            () => notification.IsActive.ShouldBeTrue(),
            () => notification.Message.ShouldBe(Message));
    }

    [Fact]
    public void WhenInactive_IsActiveIsFalse()
    {
        // Arrange
        const string Category = "auth";
        const bool IsActive = false;
        const string Message = "All clear.";

        // Act
        SystemNotification notification = new(Category, IsActive, Message);

        // Assert
        notification.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void WhenTwoNotificationsHaveSameValues_AreEqual()
    {
        // Arrange
        SystemNotification first = new("auth", true, "Authentication failed.");
        SystemNotification second = new("auth", true, "Authentication failed.");

        // Act
        bool areEqual = first == second;

        // Assert
        areEqual.ShouldBeTrue();
    }

    [Fact]
    public void WhenTwoNotificationsHaveDifferentValues_AreNotEqual()
    {
        // Arrange
        SystemNotification first = new("auth", true, "Authentication failed.");
        SystemNotification second = new("auth", false, "Authentication failed.");

        // Act
        bool areEqual = first == second;

        // Assert
        areEqual.ShouldBeFalse();
    }
}
