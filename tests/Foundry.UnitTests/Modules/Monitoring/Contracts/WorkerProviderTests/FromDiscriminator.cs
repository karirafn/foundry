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
        Result<WorkerProvider>.Success success = result.ShouldBeOfType<Result<WorkerProvider>.Success>();
        success.Value.ShouldBeOfType<WorkerProvider.GitHub>();
    }

    [Fact]
    public void WhenDiscriminatorIsGitlab_ReturnsSuccessWithGitLab()
    {
        // Arrange
        const string discriminator = "gitlab";

        // Act
        Result<WorkerProvider> result = WorkerProvider.FromDiscriminator(discriminator);

        // Assert
        Result<WorkerProvider>.Success success = result.ShouldBeOfType<Result<WorkerProvider>.Success>();
        success.Value.ShouldBeOfType<WorkerProvider.GitLab>();
    }

    [Fact]
    public void WhenDiscriminatorIsUnknown_ReturnsFailure()
    {
        // Arrange
        const string discriminator = "unknown-provider";

        // Act
        Result<WorkerProvider> result = WorkerProvider.FromDiscriminator(discriminator);

        // Assert
        Result<WorkerProvider>.Failure failure = result.ShouldBeOfType<Result<WorkerProvider>.Failure>();
        failure.Error.ShouldBe(WorkerProviderErrors.UnknownDiscriminator(discriminator));
    }
}
