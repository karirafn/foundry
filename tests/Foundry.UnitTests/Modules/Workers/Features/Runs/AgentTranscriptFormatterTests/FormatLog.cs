using Foundry.Modules.Workers.Features.Runs;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.AgentTranscriptFormatterTests;

public sealed class FormatLog
{
    [Fact]
    public void WhenRecordedStreamJsonRun_RendersReadableTranscript()
    {
        // Arrange
        string log = StreamJsonFixtures.RecordedRun;

        // Act
        string transcript = AgentTranscriptFormatter.FormatLog(log);

        // Assert
        transcript.Split('\n').ShouldBe(
        [
            "Session started (model claude-opus-5-5)",
            "→ Bash: echo hello",
            "✓ Bash (0.0s)",
            "→ Read: /workspace/nonexistent.txt",
            "✗ Read failed (0.0s): File does not exist. Note: your current working directory is /workspace.",
            "→ Agent: general-purpose — List workspace contents",
            "Subagent started: general-purpose — List workspace contents",
            "  [general-purpose] → Bash: ls /workspace",
            "  [general-purpose] ✓ Bash (0.0s)",
            "Subagent completed: List workspace contents",
            "✓ Agent (4.3s)",
            "Assistant: I ran all three steps: 1. **`echo hello`** printed `hello`. 2. **Reading `/workspace/nonexistent.txt`** failed as you expected, because the file does not exist. 3. **The general-purpose agent** ran `ls /workspace`, which listed: ``` a.txt b.txt ``` DONE",
            "Result: success · 4 turns · 10.6s · $0.16",
        ]);
    }

    [Fact]
    public void WhenRecordedStreamJsonRun_NeverRendersToolResultBodies()
    {
        // Arrange — the subagent's Bash result body is the directory listing
        string log = StreamJsonFixtures.RecordedRun;

        // Act
        string transcript = AgentTranscriptFormatter.FormatLog(log);

        // Assert
        transcript.ShouldSatisfyAllConditions(
            () => transcript.ShouldNotContain("a.txt\nb.txt"),
            () => transcript.ShouldNotContain("[Subagent hand-back]"),
            () => transcript.ShouldNotContain("\"type\""));
    }

    [Fact]
    public void WhenBootstrapOutputHasNoJson_PassesThroughUnchanged()
    {
        // Arrange
        string log = """
            2026-11-02T17:30:00.000000000Z Starting rootless dockerd (uid=1000)...
            2026-11-02T17:30:01.000000000Z FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=bootstrap failed at stage clone
            FOUNDRY_DOCKER_UNAVAILABLE detail=rootless dockerd failed to start
            """;

        // Act
        string transcript = AgentTranscriptFormatter.FormatLog(log);

        // Assert
        transcript.ShouldBe(log);
    }

    [Fact]
    public void WhenLogHasDockerTimestamps_TimestampPrefixIsKeptOnTranscriptLines()
    {
        // Arrange
        string log = """
            2026-11-02T17:31:30.100000000Z {"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"dotnet build"}}]},"parent_tool_use_id":null,"timestamp":"2026-11-02T17:31:30.100Z"}
            2026-11-02T17:31:42.600000000Z {"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"Build succeeded.","is_error":false}]},"parent_tool_use_id":null,"timestamp":"2026-11-02T17:31:42.600Z"}
            """;

        // Act
        string transcript = AgentTranscriptFormatter.FormatLog(log);

        // Assert
        transcript.Split('\n').ShouldBe(
        [
            "2026-11-02T17:31:30.100000000Z → Bash: dotnet build",
            "2026-11-02T17:31:42.600000000Z ✓ Bash (12.5s)",
        ]);
    }

    [Fact]
    public void WhenLogHasCrLfLineEndings_FormatsEachLine()
    {
        // Arrange
        string log = "plain line\r\n{\"type\":\"system\",\"subtype\":\"init\",\"model\":\"m\"}\r\n";

        // Act
        string transcript = AgentTranscriptFormatter.FormatLog(log);

        // Assert
        transcript.ShouldBe("plain line\nSession started (model m)\n");
    }
}
