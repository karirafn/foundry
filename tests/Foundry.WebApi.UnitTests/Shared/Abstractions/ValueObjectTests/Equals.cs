namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ValueObjectTests;

using Shouldly;

using Xunit;

public sealed class Equals
{
    [Fact]
    public void WhenComponentsAreIdentical_EqualsReturnsTrue()
    {
        // Arrange
        TestValueObject a = new("hello", 42);
        TestValueObject b = new("hello", 42);

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenComponentsAreIdentical_EqualityOperatorReturnsTrue()
    {
        // Arrange
        TestValueObject a = new("hello", 42);
        TestValueObject b = new("hello", 42);

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenComponentsDiffer_EqualsReturnsFalse()
    {
        // Arrange
        TestValueObject a = new("hello", 42);
        TestValueObject b = new("world", 42);

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenComponentsDiffer_InequalityOperatorReturnsTrue()
    {
        // Arrange
        TestValueObject a = new("hello", 42);
        TestValueObject b = new("hello", 99);

        // Act
        bool result = a != b;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenComparedToNull_EqualsReturnsFalse()
    {
        // Arrange
        TestValueObject a = new("hello", 42);

        // Act
        bool result = a.Equals(null);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenLeftOperandIsNull_EqualityOperatorReturnsFalse()
    {
        // Arrange
        TestValueObject? a = null;
        TestValueObject b = new("hello", 42);

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenRightOperandIsNull_EqualityOperatorReturnsFalse()
    {
        // Arrange
        TestValueObject a = new("hello", 42);
        TestValueObject? b = null;

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeFalse();
    }
}
