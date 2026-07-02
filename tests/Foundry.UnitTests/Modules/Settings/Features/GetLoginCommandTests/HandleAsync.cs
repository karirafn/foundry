using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GetLoginCommandTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenHandled_ReturnsOAuthLoginCommand()
    {
        // Arrange
        GetLoginCommand.Handler sut = new();

        // Act
        Result<OAuthLoginCommand> result = await sut.HandleAsync(
            new GetLoginCommand.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<OAuthLoginCommand>.Success>();
    }

    [Fact]
    public async Task WhenHandled_CommandContainsCredentialVolumeName()
    {
        // Arrange
        GetLoginCommand.Handler sut = new();

        // Act
        Result<OAuthLoginCommand> result = await sut.HandleAsync(
            new GetLoginCommand.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthLoginCommand>.Success success = result.ShouldBeOfType<Result<OAuthLoginCommand>.Success>();
        success.Value.Command.ShouldContain(WorkerVolumeNames.CredentialVolumeName);
    }

    [Fact]
    public async Task WhenHandled_CommandContainsLoginImageName()
    {
        // Arrange
        GetLoginCommand.Handler sut = new();

        // Act
        Result<OAuthLoginCommand> result = await sut.HandleAsync(
            new GetLoginCommand.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthLoginCommand>.Success success = result.ShouldBeOfType<Result<OAuthLoginCommand>.Success>();
        success.Value.Command.ShouldContain(WorkerImageNames.LoginImageName);
    }

    [Fact]
    public async Task WhenHandled_CommandContainsClaudeConfigContainerPath()
    {
        // Arrange
        GetLoginCommand.Handler sut = new();

        // Act
        Result<OAuthLoginCommand> result = await sut.HandleAsync(
            new GetLoginCommand.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthLoginCommand>.Success success = result.ShouldBeOfType<Result<OAuthLoginCommand>.Success>();
        success.Value.Command.ShouldContain(WorkerVolumeNames.ClaudeConfigContainerPath);
    }

    [Fact]
    public async Task WhenHandled_CommandContainsConfigDirEnvVar()
    {
        // Arrange
        GetLoginCommand.Handler sut = new();

        // Act
        Result<OAuthLoginCommand> result = await sut.HandleAsync(
            new GetLoginCommand.Query(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<OAuthLoginCommand>.Success success = result.ShouldBeOfType<Result<OAuthLoginCommand>.Success>();
        success.Value.Command.ShouldContain(WorkerVolumeNames.ClaudeConfigDirEnvVar);
    }

    [Fact]
    public void BuildLoginCommand_ReturnsExactExpectedCommand()
    {
        // Arrange
        string expected =
            $"docker run -it --rm" +
            $" -v {WorkerVolumeNames.CredentialVolumeName}:{WorkerVolumeNames.ClaudeConfigContainerPath}" +
            $" -e {WorkerVolumeNames.ClaudeConfigDirEnvVar}={WorkerVolumeNames.ClaudeConfigContainerPath}" +
            $" {WorkerImageNames.LoginImageName}" +
            $" claude /login";

        // Act
        string command = GetLoginCommand.Handler.BuildLoginCommand();

        // Assert
        command.ShouldBe(expected);
    }
}
