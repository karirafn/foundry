using Foundry.Modules.Workers.Features.Runs;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.AgentTranscriptFormatterTests;

public sealed class FormatLine
{
    private readonly AgentTranscriptFormatter _sut = new();

    [Theory]
    [InlineData("Cloning into '/workspace'...")]
    [InlineData("FOUNDRY_BOOTSTRAP_FAILED stage=auth detail=gh CLI not present")]
    [InlineData("{not json at all")]
    [InlineData("""{"type":"assistant","message":{"content":[{"type":"text","text":"cut off mid-ev""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""{"no_type":"object"}""")]
    [InlineData("")]
    public void WhenLineIsNotAStreamJsonEvent_PassesThroughUnchanged(string line)
    {
        // Act
        IReadOnlyList<string> result = _sut.FormatLine(line);

        // Assert
        result.ShouldBe([line]);
    }

    [Theory]
    [InlineData("""{"type":"system","subtype":"thinking_tokens","tokens":128}""")]
    [InlineData("""{"type":"system","subtype":"hook_started","hook_name":"SessionStart:startup"}""")]
    [InlineData("""{"type":"system","subtype":"task_progress","task_id":"t","description":"Running"}""")]
    [InlineData("""{"type":"system","subtype":"brand_new_subtype"}""")]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed"}}""")]
    [InlineData("""{"type":"brand_new_event_type","payload":{}}""")]
    [InlineData("""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"secret plan"}]}}""")]
    [InlineData("""{"type":"user","message":{"content":[{"type":"text","text":"subagent prompt"}]}}""")]
    public void WhenEventIsNoiseOrUnknown_IsOmitted(string line)
    {
        // Act
        IReadOnlyList<string> result = _sut.FormatLine(line);

        // Assert
        result.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Bash", """{"command":"git push origin HEAD","description":"Push"}""", "→ Bash: git push origin HEAD")]
    [InlineData("Read", """{"file_path":"/workspace/src/A.cs"}""", "→ Read: /workspace/src/A.cs")]
    [InlineData("Edit", """{"file_path":"/workspace/src/B.cs","old_string":"x","new_string":"y"}""", "→ Edit: /workspace/src/B.cs")]
    [InlineData("Write", """{"file_path":"/workspace/src/C.cs","content":"class C {}"}""", "→ Write: /workspace/src/C.cs")]
    [InlineData("Agent", """{"subagent_type":"Explore","description":"Find parser","prompt":"long prompt"}""", "→ Agent: Explore — Find parser")]
    [InlineData("Skill", """{"skill":"code-review","args":"high"}""", "→ Skill: code-review")]
    [InlineData("TodoWrite", """{"todos":[{"content":"secret"}]}""", "→ TodoWrite")]
    public void WhenAssistantCallsTool_RendersToolNameAndInputSummary(string name, string input, string expected)
    {
        // Arrange
        string line = $$$"""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"{{{name}}}","input":{{{input}}}}]}}""";

        // Act
        IReadOnlyList<string> result = _sut.FormatLine(line);

        // Assert
        result.ShouldBe([expected]);
    }

    [Fact]
    public void WhenBashCommandIsLongOrMultiline_IsCollapsedAndTruncated()
    {
        // Arrange
        string command = "echo start\\n" + new string('x', 300);
        string line = $$$"""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"{{{command}}}"}}]}}""";

        // Act
        IReadOnlyList<string> result = _sut.FormatLine(line);

        // Assert
        string rendered = result.ShouldHaveSingleItem();
        rendered.ShouldSatisfyAllConditions(
            () => rendered.ShouldStartWith("→ Bash: echo start xxx"),
            () => rendered.ShouldEndWith("…"),
            () => rendered.ShouldNotContain("\n"),
            () => rendered.Length.ShouldBeLessThan(220));
    }

    [Fact]
    public void WhenToolFails_RendersTruncatedErrorWithDuration()
    {
        // Arrange
        string error = "Exit code 1\\n" + new string('e', 400);
        _sut.FormatLine("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t9","name":"Bash","input":{"command":"dotnet test"}}]},"timestamp":"2026-11-02T10:00:00.000Z"}""");

        // Act
        IReadOnlyList<string> result = _sut.FormatLine(
            $$"""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t9","content":"{{error}}","is_error":true}]},"timestamp":"2026-11-02T10:02:05.000Z"}""");

        // Assert
        string rendered = result.ShouldHaveSingleItem();
        rendered.ShouldSatisfyAllConditions(
            () => rendered.ShouldStartWith("✗ Bash failed (2m 05s): Exit code 1 eee"),
            () => rendered.ShouldEndWith("…"),
            () => rendered.Length.ShouldBeLessThan(260));
    }

    [Fact]
    public void WhenToolSucceeds_OmitsResultBody()
    {
        // Arrange
        _sut.FormatLine("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t2","name":"Read","input":{"file_path":"/workspace/.env"}}]}}""");

        // Act
        IReadOnlyList<string> result = _sut.FormatLine(
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t2","content":"DB_PASSWORD=hunter2","is_error":false}]}}""");

        // Assert
        result.ShouldBe(["✓ Read"]);
    }

    [Fact]
    public void WhenToolResultHasNoMatchingToolUse_RendersGenericName()
    {
        // Arrange — the tool_use was before the stored log's truncated head

        // Act
        IReadOnlyList<string> result = _sut.FormatLine(
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"gone","content":[{"type":"text","text":"boom"}],"is_error":true}]}}""");

        // Assert
        result.ShouldBe(["✗ tool failed: boom"]);
    }

    [Fact]
    public void WhenAssistantText_RendersSingleLine()
    {
        // Arrange
        string line = """{"type":"assistant","message":{"content":[{"type":"text","text":"Opening the PR.\n\nAll checks green."}]}}""";

        // Act
        IReadOnlyList<string> result = _sut.FormatLine(line);

        // Assert
        result.ShouldBe(["Assistant: Opening the PR. All checks green."]);
    }

    [Fact]
    public void WhenSubagentStartsAndFinishes_RendersBothWithDescription()
    {
        // Act
        IReadOnlyList<string> started = _sut.FormatLine(
            """{"type":"system","subtype":"task_started","task_id":"a1","tool_use_id":"t5","description":"Review diff","subagent_type":"code-reviewer"}""");
        IReadOnlyList<string> finished = _sut.FormatLine(
            """{"type":"system","subtype":"task_notification","task_id":"a1","tool_use_id":"t5","status":"failed","summary":"long summary body"}""");

        // Assert
        started.ShouldBe(["Subagent started: code-reviewer — Review diff"]);
        finished.ShouldBe(["Subagent failed: Review diff"]);
    }

    [Fact]
    public void WhenEventComesFromSubagent_IsIndentedWithSubagentLabel()
    {
        // Arrange
        _sut.FormatLine("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"agent-1","name":"Agent","input":{"subagent_type":"Explore","description":"Look"}}]},"parent_tool_use_id":null}""");

        // Act
        IReadOnlyList<string> result = _sut.FormatLine(
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t7","name":"Read","input":{"file_path":"/workspace/x.cs"}}]},"parent_tool_use_id":"agent-1"}""");

        // Assert
        result.ShouldBe(["  [Explore] → Read: /workspace/x.cs"]);
    }

    [Fact]
    public void WhenResultIsError_RendersErrorSummary()
    {
        // Arrange
        string line = """{"is_error":true,"subtype":"error_max_turns","num_turns":200,"duration_ms":3723000,"total_cost_usd":12.345,"type":"result"}""";

        // Act
        IReadOnlyList<string> result = _sut.FormatLine(line);

        // Assert
        result.ShouldBe(["Result: error (error_max_turns) · 200 turns · 62m 03s · $12.35"]);
    }

    [Fact]
    public void WhenDecodedTextContainsSecretShape_IsRedacted()
    {
        // Arrange — a JSON-escaped token boundary the raw-line redactor would not see as one token
        string line = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t3","name":"Bash","input":{"command":"git remote add o https://x:ghp_abc123@github.com/o/r"}}]}}""";

        // Act
        IReadOnlyList<string> result = _sut.FormatLine(line);

        // Assert
        string rendered = result.ShouldHaveSingleItem();
        rendered.ShouldSatisfyAllConditions(
            () => rendered.ShouldNotContain("ghp_abc123"),
            () => rendered.ShouldNotContain("x:ghp"));
    }

    [Fact]
    public void WhenAssistantEventHasSeveralBlocks_RendersOneLinePerMeaningfulBlock()
    {
        // Arrange
        string line = """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"hmm"},{"type":"text","text":"Running tests."},{"type":"tool_use","id":"t4","name":"Bash","input":{"command":"dotnet test"}}]}}""";

        // Act
        IReadOnlyList<string> result = _sut.FormatLine(line);

        // Assert
        result.ShouldBe(["Assistant: Running tests.", "→ Bash: dotnet test"]);
    }
}
