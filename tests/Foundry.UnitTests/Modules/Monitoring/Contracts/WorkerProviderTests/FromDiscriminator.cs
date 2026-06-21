using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.WorkerProviderTests;

public sealed class FromDiscriminator
{
    [Fact]
    public void WhenDiscriminatorIsGithub_ReturnsGitHub()
    {
        // Arrange
        const string discriminator = "github";

        // Act
        WorkerProvider result = WorkerProvider.FromDiscriminator(discriminator);

        // Assert
        result.ShouldBeOfType<WorkerProvider.GitHub>();
    }

    [Fact]
    public void WhenDiscriminatorIsGitlab_ReturnsGitLab()
    {
        // Arrange
        const string discriminator = "gitlab";

        // Act
        WorkerProvider result = WorkerProvider.FromDiscriminator(discriminator);

        // Assert
        result.ShouldBeOfType<WorkerProvider.GitLab>();
    }

    [Fact]
    public void WhenDiscriminatorIsUnknown_Throws()
    {
        // Arrange
        const string discriminator = "unknown-provider";

        // Act / Assert
        Should.Throw<ArgumentException>(() => WorkerProvider.FromDiscriminator(discriminator));
    }
}
