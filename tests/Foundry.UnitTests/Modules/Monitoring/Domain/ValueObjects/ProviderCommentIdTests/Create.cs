using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.ProviderCommentIdTests;

public sealed class Create
{
    [Fact]
    public void WhenValueIsPositive_Succeeds()
    {
        // Act
        Result<ProviderCommentId> result = ProviderCommentId.Create(42);

        // Assert
        result.ValueOrThrow().Value.ShouldBe(42);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WhenValueIsNotPositive_Fails(long value)
    {
        // Act
        Result<ProviderCommentId> result = ProviderCommentId.Create(value);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(ProviderCommentIdErrors.NotPositiveCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void WhenProviderValueIsAbsentOrInvalid_FromProviderIsNull(long? value)
    {
        // Act
        ProviderCommentId? id = ProviderCommentId.FromProvider(value);

        // Assert
        id.ShouldBeNull();
    }

    [Fact]
    public void WhenProviderValueIsPositive_FromProviderCarriesIt()
    {
        // Act
        ProviderCommentId? id = ProviderCommentId.FromProvider(7);

        // Assert
        id.ShouldBe(ProviderCommentId.Create(7).ValueOrThrow());
    }
}
