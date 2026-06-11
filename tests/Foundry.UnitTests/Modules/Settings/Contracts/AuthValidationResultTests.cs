using Foundry.Modules.Settings.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Contracts;

public sealed class AuthValidationResultTests
{
    [Fact]
    public void WhenValid_HasExpectedProperties()
    {
        // Arrange & Act
        AuthValidationResult result = AuthValidationResult.Valid();

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeTrue(),
            () => result.PassedOptimistically.ShouldBeFalse(),
            () => result.ErrorMessage.ShouldBeNull());
    }

    [Fact]
    public void WhenValidOptimistic_HasExpectedProperties()
    {
        // Arrange & Act
        AuthValidationResult result = AuthValidationResult.ValidOptimistic();

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeTrue(),
            () => result.PassedOptimistically.ShouldBeTrue(),
            () => result.ErrorMessage.ShouldBeNull());
    }

    [Fact]
    public void WhenInvalid_HasExpectedProperties()
    {
        // Arrange
        const string message = "OAuth token expired — run `claude setup-token` to generate a new one";

        // Act
        AuthValidationResult result = AuthValidationResult.Invalid(message);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsValid.ShouldBeFalse(),
            () => result.PassedOptimistically.ShouldBeFalse(),
            () => result.ErrorMessage.ShouldBe(message));
    }
}
