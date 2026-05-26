using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Persistence;
using Foundry.WebApi.UnitTests.Shared.Abstractions.EntityTests;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Shared.Persistence.StronglyTypedIdValueConverterTests;

public sealed class ConvertToProvider
{
    [Fact]
    public void GivenStronglyTypedId_ConvertsToUnderlyingGuid()
    {
        // Arrange
        Guid expected = Guid.NewGuid();
        TestId id = TestId.From(expected);
        StronglyTypedIdValueConverter<TestId> converter = new();
        Func<TestId, Guid> toProvider = (Func<TestId, Guid>)converter.ConvertToProviderExpression.Compile();

        // Act
        Guid result = toProvider(id);

        // Assert
        result.ShouldBe(expected);
    }
}
