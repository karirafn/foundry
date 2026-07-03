using Foundry.Modules.Workers.Features.Login;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginExecCommandTests;

public sealed class BuildArgv
{
    [Fact]
    public void WhenCodeProvided_ReturnsCorrectArgv()
    {
        // Arrange
        const string code = "abc123";

        // Act
        LoginExecCommand command = LoginExecCommand.ForCode(code);

        // Assert
        command.Argv.ShouldBe(
            ["sh", "-c", $"printf '%s\\n' \"$C\" > {LoginExecCommand.FifoPath}; kill $(cat {LoginExecCommand.SleepPidPath} 2>/dev/null) 2>/dev/null || true"]);
    }

    [Fact]
    public void WhenCodeProvided_EnvCarriesCode()
    {
        // Arrange
        const string code = "mycode";

        // Act
        LoginExecCommand command = LoginExecCommand.ForCode(code);

        // Assert
        command.Env.ShouldContain($"C={code}");
    }

    [Fact]
    public void WhenCodeContainsShellMetacharacters_DoesNotInterpolateIntoArgv()
    {
        // Arrange
        const string code = "a;rm -rf/#state";

        // Act
        LoginExecCommand command = LoginExecCommand.ForCode(code);

        // Assert
        // The argv must be unchanged (code is NOT in the command string)
        command.Argv.ShouldBe(
            ["sh", "-c", $"printf '%s\\n' \"$C\" > {LoginExecCommand.FifoPath}; kill $(cat {LoginExecCommand.SleepPidPath} 2>/dev/null) 2>/dev/null || true"]);

        // The code is in the env, not in the command string
        command.Env.ShouldContain($"C={code}");
    }
}
