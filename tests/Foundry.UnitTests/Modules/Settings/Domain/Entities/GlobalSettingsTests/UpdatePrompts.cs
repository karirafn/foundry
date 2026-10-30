using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdatePrompts
{
    [Fact]
    public void WhenValidPromptsProvided_AssignsWorkerPrompts()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        Result<WorkerPrompts> promptsResult = WorkerPrompts.Create(
            "System #{issueNumber}",
            "Worker #{issueNumber}",
            "Use kebab-case");
        WorkerPrompts prompts = promptsResult.ShouldBeOfType<Result<WorkerPrompts>.Success>().Value;

        // Act
        settings.UpdatePrompts(prompts);

        // Assert
        settings.WorkerPrompts.ShouldBe(prompts);
    }

    [Fact]
    public void WhenValidPromptsProvided_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;
        Result<WorkerPrompts> promptsResult = WorkerPrompts.Create(
            "System #{issueNumber}",
            "Worker #{issueNumber}",
            "Use kebab-case");
        WorkerPrompts prompts = promptsResult.ShouldBeOfType<Result<WorkerPrompts>.Success>().Value;

        // Act
        settings.UpdatePrompts(prompts);

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }
}
