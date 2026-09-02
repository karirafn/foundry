using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.FailureCategoryTests;

public sealed class Members
{
    [Fact]
    public void NonZeroExit_ValueMatchesToken()
    {
        // Arrange

        // Act
        FailureCategory category = FailureCategory.NonZeroExit;

        // Assert
        category.Value.ShouldBe(FailureCategory.NonZeroExitToken);
    }

    [Fact]
    public void TimedOut_ValueMatchesToken()
    {
        // Arrange

        // Act
        FailureCategory category = FailureCategory.TimedOut;

        // Assert
        category.Value.ShouldBe(FailureCategory.TimedOutToken);
    }

    [Fact]
    public void ContainerError_ValueMatchesToken()
    {
        // Arrange

        // Act
        FailureCategory category = FailureCategory.ContainerError;

        // Assert
        category.Value.ShouldBe(FailureCategory.ContainerErrorToken);
    }

    [Fact]
    public void UsageLimited_ValueMatchesToken()
    {
        // Arrange

        // Act
        FailureCategory category = FailureCategory.UsageLimited;

        // Assert
        category.Value.ShouldBe(FailureCategory.UsageLimitedToken);
    }

    [Fact]
    public void WorkerBootstrapFailed_ValueMatchesToken()
    {
        // Arrange

        // Act
        FailureCategory category = FailureCategory.WorkerBootstrapFailed;

        // Assert
        category.Value.ShouldBe(FailureCategory.WorkerBootstrapFailedToken);
    }

    [Fact]
    public void AuthInvalid_ValueMatchesToken()
    {
        // Arrange

        // Act
        FailureCategory category = FailureCategory.AuthInvalid;

        // Assert
        category.Value.ShouldBe(FailureCategory.AuthInvalidToken);
    }

    [Fact]
    public void ProviderError_ValueMatchesToken()
    {
        // Arrange

        // Act
        FailureCategory category = FailureCategory.ProviderError;

        // Assert
        category.Value.ShouldBe(FailureCategory.ProviderErrorToken);
    }

    [Fact]
    public void TransientApiError_ValueMatchesToken()
    {
        // Arrange

        // Act
        FailureCategory category = FailureCategory.TransientApiError;

        // Assert
        category.Value.ShouldBe(FailureCategory.TransientApiErrorToken);
    }

    [Fact]
    public void CreditsExhausted_ValueMatchesToken()
    {
        // Arrange

        // Act
        FailureCategory category = FailureCategory.CreditsExhausted;

        // Assert
        category.Value.ShouldBe(FailureCategory.CreditsExhaustedToken);
    }

    [Fact]
    public void PrClosed_ValueMatchesToken()
    {
        // Arrange

        // Act
        FailureCategory category = FailureCategory.PrClosed;

        // Assert
        category.Value.ShouldBe(FailureCategory.PrClosedToken);
    }

    [Fact]
    public void WhenTwoInstancesHaveSameToken_AreEqual()
    {
        // Arrange
        FailureCategory a = FailureCategory.PrClosed;
        FailureCategory b = FailureCategory.FromToken(FailureCategory.PrClosedToken);

        // Act

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenTwoInstancesHaveDifferentTokens_AreNotEqual()
    {
        // Arrange
        FailureCategory a = FailureCategory.PrClosed;
        FailureCategory b = FailureCategory.NonZeroExit;

        // Act

        // Assert
        a.ShouldNotBe(b);
    }

    [Fact]
    public void ToString_ReturnsTokenValue()
    {
        // Arrange
        FailureCategory category = FailureCategory.TransientApiError;

        // Act
        string result = category.ToString();

        // Assert
        result.ShouldBe(FailureCategory.TransientApiErrorToken);
    }

    [Fact]
    public void WhenFromTokenCalledWithUnknownToken_ThrowsInvalidOperationException()
    {
        // Arrange
        const string unknownToken = "bogus_token";

        // Act
        Action act = () => FailureCategory.FromToken(unknownToken);

        // Assert
        Should.Throw<InvalidOperationException>(act);
    }
}
