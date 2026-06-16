using Foundry.Modules.Settings.Domain;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.GlobalSettingsTests;

public sealed class UpdatePromptTemplates
{
    [Fact]
    public void WhenBothTemplatesProvided_ReturnsSuccess()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdatePromptTemplates("system prompt", "worker prompt");

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

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
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        // (no action — testing initial state)

        // Assert
        settings.SystemPromptTemplate.ShouldBeNull();
    }

    [Fact]
    public void WhenCreated_WorkerPromptTemplateIsNull()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        // (no action — testing initial state)

        // Assert
        settings.WorkerPromptTemplate.ShouldBeNull();
    }

    [Fact]
    public void WhenSystemPromptTemplateIsEmpty_ReturnsFailure()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdatePromptTemplates(string.Empty, "worker prompt");

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenSystemPromptTemplateIsEmpty_DoesNotMutateState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.UpdatePromptTemplates(string.Empty, "worker prompt");

        // Assert
        settings.SystemPromptTemplate.ShouldBeNull();
    }

    [Fact]
    public void WhenWorkerPromptTemplateIsEmpty_ReturnsFailure()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdatePromptTemplates("system prompt", string.Empty);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenWorkerPromptTemplateIsEmpty_DoesNotMutateState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.UpdatePromptTemplates("system prompt", string.Empty);

        // Assert
        settings.WorkerPromptTemplate.ShouldBeNull();
    }

    [Fact]
    public void WhenSystemPromptTemplateExceedsMaxLength_ReturnsFailure()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        string tooLong = new('a', GlobalSettings.MaxPromptTemplateLength + 1);

        // Act
        Result result = settings.UpdatePromptTemplates(tooLong, "worker prompt");

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenWorkerPromptTemplateExceedsMaxLength_ReturnsFailure()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        string tooLong = new('a', GlobalSettings.MaxPromptTemplateLength + 1);

        // Act
        Result result = settings.UpdatePromptTemplates("system prompt", tooLong);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenSystemPromptTemplateIsAtMaxLength_ReturnsSuccess()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        string atMax = new('a', GlobalSettings.MaxPromptTemplateLength);

        // Act
        Result result = settings.UpdatePromptTemplates(atMax, "worker prompt");

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenWorkerPromptTemplateIsAtMaxLength_ReturnsSuccess()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        string atMax = new('a', GlobalSettings.MaxPromptTemplateLength);

        // Act
        Result result = settings.UpdatePromptTemplates("system prompt", atMax);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
