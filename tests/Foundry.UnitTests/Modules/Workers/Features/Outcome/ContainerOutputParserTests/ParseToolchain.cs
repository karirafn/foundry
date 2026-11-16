using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ContainerOutputParserTests;

public sealed class ParseToolchain
{
    private const string ToolchainLine =
        """2026-11-16T10:00:01.000000000Z FOUNDRY_TOOLCHAIN [{"name":"node","version":"22.22.3"},{"name":"git","version":"2.47.3"}]""";

    private readonly IContainerOutputParser _sut = new ContainerOutputParser(
        NullLogger<ContainerOutputParser>.Instance);

    [Fact]
    public void WhenTheLogHoldsOneToolchainLine_ReturnsItsEntriesInOrder()
    {
        // Arrange
        string log = "2026-11-16T10:00:00.000000000Z Worker role: implement\n"
            + ToolchainLine + "\n"
            + """2026-11-16T10:00:02.000000000Z {"type":"result","subtype":"success","is_error":false}""";

        // Act
        Toolchain? toolchain = _sut.ParseToolchain(log);

        // Assert
        toolchain.ShouldNotBeNull().Entries.Select(entry => (entry.Name, entry.Version)).ShouldBe(
            [("node", "22.22.3"), ("git", "2.47.3")]);
    }

    [Fact]
    public void WhenTheLineIsFollowedByMoreThanTheParsedTail_StillReadsIt()
    {
        // Arrange — the line is written at startup; a long run's output pushes it out of the 64 KB tail.
        string longOutput = string.Join('\n', Enumerable.Repeat(
            """2026-11-16T10:00:02.000000000Z {"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}""",
            2_000));
        string log = ToolchainLine + "\n" + longOutput;

        // Act
        Toolchain? toolchain = _sut.ParseToolchain(log);

        // Assert
        toolchain.ShouldNotBeNull().Entries.Count.ShouldBe(2);
    }

    [Fact]
    public void WhenTheLogHoldsNoToolchainLine_ReturnsNull()
    {
        // Arrange — bootstrap failed before the script ran.
        string log = "2026-11-16T10:00:00.000000000Z FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=x";

        // Act
        Toolchain? toolchain = _sut.ParseToolchain(log);

        // Assert
        toolchain.ShouldBeNull();
    }

    [Fact]
    public void WhenTheLineAppearsTwice_ReturnsNull()
    {
        // Arrange
        string log = ToolchainLine + "\n" + ToolchainLine;

        // Act
        Toolchain? toolchain = _sut.ParseToolchain(log);

        // Assert
        toolchain.ShouldBeNull();
    }

    [Fact]
    public void WhenTheLineIsMalformed_ReturnsNull()
    {
        // Arrange
        string log = "2026-11-16T10:00:01.000000000Z FOUNDRY_TOOLCHAIN [{\"name\":\"node\"";

        // Act
        Toolchain? toolchain = _sut.ParseToolchain(log);

        // Assert
        toolchain.ShouldBeNull();
    }
}
