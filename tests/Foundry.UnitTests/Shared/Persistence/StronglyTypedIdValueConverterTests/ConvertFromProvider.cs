using Foundry.Shared.Infrastructure;
using Foundry.UnitTests.Shared.Abstractions.EntityTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Persistence.StronglyTypedIdValueConverterTests;

public sealed class ConvertFromProvider
{
    [Fact]
    public void GivenGuid_ConvertsToStronglyTypedId()
    {
        // Arrange
        Guid guid = Guid.NewGuid();
        StronglyTypedIdValueConverter<TestId> converter = new();
        Func<Guid, TestId> fromProvider = (Func<Guid, TestId>)converter.ConvertFromProviderExpression.Compile();

        // Act
        TestId result = fromProvider(guid);

        // Assert
        result.Value.ShouldBe(guid);
    }
}
