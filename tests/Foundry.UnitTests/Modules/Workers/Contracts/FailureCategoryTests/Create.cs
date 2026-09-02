using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.FailureCategoryTests;

public sealed class Create
{
    [Theory]
    [InlineData(FailureCategory.NonZeroExitToken)]
    [InlineData(FailureCategory.TimedOutToken)]
    [InlineData(FailureCategory.ContainerErrorToken)]
    [InlineData(FailureCategory.UsageLimitedToken)]
    [InlineData(FailureCategory.WorkerBootstrapFailedToken)]
    [InlineData(FailureCategory.AuthInvalidToken)]
    [InlineData(FailureCategory.ProviderErrorToken)]
    [InlineData(FailureCategory.TransientApiErrorToken)]
    [InlineData(FailureCategory.CreditsExhaustedToken)]
    [InlineData(FailureCategory.PrClosedToken)]
    public void WhenKnownToken_ReturnsSuccess(string token)
    {
        // Arrange

        // Act
        Result<FailureCategory> result = FailureCategory.Create(token);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(FailureCategory.NonZeroExitToken)]
    [InlineData(FailureCategory.TimedOutToken)]
    [InlineData(FailureCategory.ContainerErrorToken)]
    [InlineData(FailureCategory.UsageLimitedToken)]
    [InlineData(FailureCategory.WorkerBootstrapFailedToken)]
    [InlineData(FailureCategory.AuthInvalidToken)]
    [InlineData(FailureCategory.ProviderErrorToken)]
    [InlineData(FailureCategory.TransientApiErrorToken)]
    [InlineData(FailureCategory.CreditsExhaustedToken)]
    [InlineData(FailureCategory.PrClosedToken)]
    public void WhenKnownToken_ValueMatchesToken(string token)
    {
        // Arrange

        // Act
        Result<FailureCategory> result = FailureCategory.Create(token);

        // Assert
        Result<FailureCategory>.Success success = result.ShouldBeOfType<Result<FailureCategory>.Success>();
        success.Value.Value.ShouldBe(token);
    }

    [Theory]
    [InlineData("unknown_token")]
    [InlineData("generic_failure")]
    [InlineData("")]
    [InlineData("NON_ZERO_EXIT")]
    public void WhenUnknownToken_ReturnsFailure(string token)
    {
        // Arrange

        // Act
        Result<FailureCategory> result = FailureCategory.Create(token);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenUnknownToken_ErrorCodeIsFailureCategoryUnknown()
    {
        // Arrange
        const string token = "generic_failure";

        // Act
        Result<FailureCategory> result = FailureCategory.Create(token);

        // Assert
        Result<FailureCategory>.Failure failure = result.ShouldBeOfType<Result<FailureCategory>.Failure>();
        failure.Error.Code.ShouldBe("FailureCategory.Unknown");
    }
}
