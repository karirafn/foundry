namespace Foundry.WebApi.UnitTests.Shared.Abstractions.EntityTests;

using Foundry.Shared;

using Shouldly;

using Xunit;

public sealed class Equals
{
    [Fact]
    public void WhenSameIdType_IdsWithSameValueAreEqual()
    {
        // Arrange
        Guid guid = Guid.NewGuid();
        TestId a = TestId.From(guid);
        TestId b = TestId.From(guid);

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenSameTypeAndSameId_EntitiesAreEqual()
    {
        // Arrange
        TestId id = TestId.From(Guid.NewGuid());
        TestEntity a = new(id);
        TestEntity b = new(id);

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenSameTypeAndDifferentId_EntitiesAreNotEqual()
    {
        // Arrange
        TestEntity a = new(TestId.From(Guid.NewGuid()));
        TestEntity b = new(TestId.From(Guid.NewGuid()));

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenDifferentTypeAndSameId_EntitiesAreNotEqual()
    {
        // Arrange
        TestId id = TestId.From(Guid.NewGuid());
        TestEntity a = new(id);
        OtherTestEntity b = new(id);

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenSameTypeAndSameId_EqualityOperatorReturnsTrue()
    {
        // Arrange
        TestId id = TestId.From(Guid.NewGuid());
        TestEntity a = new(id);
        TestEntity b = new(id);

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenSameTypeAndDifferentId_InequalityOperatorReturnsTrue()
    {
        // Arrange
        TestEntity a = new(TestId.From(Guid.NewGuid()));
        TestEntity b = new(TestId.From(Guid.NewGuid()));

        // Act
        bool result = a != b;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenAggregateRootHasSameTypeAndSameId_AreEqual()
    {
        // Arrange
        TestId id = TestId.From(Guid.NewGuid());
        TestAggregateRoot a = new(id);
        TestAggregateRoot b = new(id);

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenAggregateRootHasSameTypeAndDifferentId_AreNotEqual()
    {
        // Arrange
        TestAggregateRoot a = new(TestId.From(Guid.NewGuid()));
        TestAggregateRoot b = new(TestId.From(Guid.NewGuid()));

        // Act
        bool result = a.Equals(b);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenOtherIsNull_EqualsReturnsFalse()
    {
        // Arrange
        TestEntity entity = new(TestId.From(Guid.NewGuid()));

        // Act
        bool result = entity.Equals(null);

        // Assert
        result.ShouldBeFalse();
    }
}
