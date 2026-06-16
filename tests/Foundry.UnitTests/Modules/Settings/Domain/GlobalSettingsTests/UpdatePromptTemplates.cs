using Foundry.Modules.Settings.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class UpdatePromptTemplates
{
    [Fact]
    public void WhenBothTemplatesProvided_SetsSystemPromptTemplate()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.UpdatePromptTemplates("system prompt", "worker prompt");

        // Assert
        settings.SystemPromptTemplate.ShouldBe("system prompt");
    }

    [Fact]
    public void WhenBothTemplatesProvided_SetsWorkerPromptTemplate()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.UpdatePromptTemplates("system prompt", "worker prompt");

        // Assert
        settings.WorkerPromptTemplate.ShouldBe("worker prompt");
    }

    [Fact]
    public void WhenBothTemplatesProvided_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.UpdatePromptTemplates("system prompt", "worker prompt");

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenSystemPromptTemplateIsNull_SetsSystemPromptTemplateToNull()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdatePromptTemplates("system prompt", "worker prompt");

        // Act
        settings.UpdatePromptTemplates(null, "worker prompt");

        // Assert
        settings.SystemPromptTemplate.ShouldBeNull();
    }

    [Fact]
    public void WhenWorkerPromptTemplateIsNull_SetsWorkerPromptTemplateToNull()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdatePromptTemplates("system prompt", "worker prompt");

        // Act
        settings.UpdatePromptTemplates("system prompt", null);

        // Assert
        settings.WorkerPromptTemplate.ShouldBeNull();
    }

    [Fact]
    public void WhenCreated_SystemPromptTemplateIsNull()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.SystemPromptTemplate.ShouldBeNull();
    }

    [Fact]
    public void WhenCreated_WorkerPromptTemplateIsNull()
    {
        // Arrange & Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.WorkerPromptTemplate.ShouldBeNull();
    }
}
