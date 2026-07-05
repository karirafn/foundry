using Foundry.Modules.Credentials.Features.Login;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginExecCommandTests;

public sealed class BuildArgv
{
    [Fact]
    public void WhenForCodeCalled_ArgvWritesEnvVarToFifo()
    {
        // Arrange
        // Act
        LoginExecCommand command = LoginExecCommand.ForCode();

        // Assert — argv writes the $C env var into the FIFO, then kills the sleep PID
        command.Argv.ShouldBe(
            [
                "sh",
                "-c",
                $"printf '%s\\n' \"$C\" > {LoginExecCommand.FifoPath}; kill $(cat {LoginExecCommand.SleepPidPath} 2>/dev/null) 2>/dev/null || true",
            ]);
    }

    [Fact]
    public void WhenForCodeCalled_ArgvDoesNotContainCodeLiteral()
    {
        // Arrange
        const string code = "super-secret-oauth-code";

        // Act
        LoginExecCommand command = LoginExecCommand.ForCode();

        // Assert — the code value is NOT interpolated into argv (it travels via the C env var)
        string joined = string.Join(" ", command.Argv);
        joined.ShouldNotContain(code);
    }

    [Fact]
    public void WhenForCodeCalledWithMetacharacters_ArgvDoesNotContainCode()
    {
        // Arrange
        const string codeWithMeta = @"abc$'"";&|";

        // Act
        LoginExecCommand command = LoginExecCommand.ForCode();

        // Assert — shell metacharacters in the code stay in the env var, not in argv
        string joined = string.Join(" ", command.Argv);
        joined.ShouldNotContain(codeWithMeta);
    }
}
