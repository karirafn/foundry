using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.WorkerProviderTests;

public sealed class FromDiscriminator
{
    [Fact]
    public void WhenDiscriminatorIsGithub_ReturnsSuccessWithGitHub()
    {
        // Arrange
        const string discriminator = "github";

        // Act
        Result<WorkerProvider> result = WorkerProvider.FromDiscriminator(discriminator);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ((Result<WorkerProvider>.Success)result).Value.ShouldBeOfType<WorkerProvider.GitHub>();
    }

    [Fact]
    public void WhenDiscriminatorIsGitlab_ReturnsSuccessWithGitLab()
    {
        // Arrange
        const string discriminator = "gitlab";

        // Act
        Result<WorkerProvider> result = WorkerProvider.FromDiscriminator(discriminator);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        ((Result<WorkerProvider>.Success)result).Value.ShouldBeOfType<WorkerProvider.GitLab>();
    }

    [Fact]
    public void WhenDiscriminatorIsUnknown_ReturnsFailure()
    {
        // Arrange
        const string discriminator = "unknown-provider";

        // Act
        Result<WorkerProvider> result = WorkerProvider.FromDiscriminator(discriminator);

        // Assert
        result.IsFailure.ShouldBeTrue();
        ((Result<WorkerProvider>.Failure)result).Error.ShouldBe(
            WorkerProviderErrors.UnknownDiscriminator(discriminator));
    }
}
