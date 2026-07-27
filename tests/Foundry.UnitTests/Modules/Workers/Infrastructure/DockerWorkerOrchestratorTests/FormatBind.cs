using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DockerWorkerOrchestratorTests;

public sealed class FormatBind
{
    [Fact]
    public void WhenReadOnlyIsFalse_DoesNotAppendRo()
    {
        // Arrange
        BindMount mount = new("/host/data", "/container/data", ReadOnly: false);

        // Act
        string result = DockerWorkerOrchestrator.FormatBind(mount);

        // Assert
        result.ShouldBe("/host/data:/container/data");
    }

    [Fact]
    public void WhenReadOnlyIsTrue_AppendsRo()
    {
        // Arrange
        BindMount mount = new("/host/config", "/container/config", ReadOnly: true);

        // Act
        string result = DockerWorkerOrchestrator.FormatBind(mount);

        // Assert
        result.ShouldBe("/host/config:/container/config:ro");
    }
}
