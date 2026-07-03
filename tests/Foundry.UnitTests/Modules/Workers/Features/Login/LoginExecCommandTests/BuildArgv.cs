using Foundry.Modules.Workers.Features.Login;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginExecCommandTests;

public sealed class BuildArgv
{
    [Fact]
    public void WhenForStdinCalled_ArgvReadsFromStdinToFifo()
    {
        // Arrange
        // Act
        LoginExecCommand command = LoginExecCommand.ForStdin();

        // Assert — argv reads stdin (cat) into the FIFO, then kills the sleep PID
        command.Argv.ShouldBe(
            ["sh", "-c", $"cat > {LoginExecCommand.FifoPath}; kill $(cat {LoginExecCommand.SleepPidPath} 2>/dev/null) 2>/dev/null || true"]);
    }

    [Fact]
    public void WhenForStdinCalled_ArgvDoesNotContainCodeLiteral()
    {
        // Arrange
        // Act
        LoginExecCommand command = LoginExecCommand.ForStdin();

        // Assert — no code literal anywhere in argv (code is written via STDIN, not embedded)
        string joined = string.Join(" ", command.Argv);
        joined.ShouldNotContain("code");
        joined.ShouldNotContain("printf");
    }
}
