namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ValueObjectTests;

using Shouldly;

using Xunit;

public sealed class GetHashCode
{
    [Fact]
    public void WhenObjectsAreEqual_HashCodesAreEqual()
    {
        // Arrange
        TestValueObject a = new("hello", 42);
        TestValueObject b = new("hello", 42);

        // Act
        int hashA = a.GetHashCode();
        int hashB = b.GetHashCode();

        // Assert
        hashA.ShouldBe(hashB);
    }

    [Fact]
    public void WhenObjectsDiffer_HashCodesAreDifferent()
    {
        // Arrange
        TestValueObject a = new("hello", 42);
        TestValueObject b = new("world", 99);

        // Act
        int hashA = a.GetHashCode();
        int hashB = b.GetHashCode();

        // Assert
        // Probabilistic: unequal objects may share hash codes, but shouldn't for these values
        hashA.ShouldNotBe(hashB);
    }
}
