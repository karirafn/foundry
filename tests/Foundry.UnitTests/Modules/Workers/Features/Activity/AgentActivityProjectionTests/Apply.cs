using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Activity;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Activity.AgentActivityProjectionTests;

public sealed class Apply
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly AgentActivityProjection _sut = new();

    [Theory]
    [InlineData("Cloning into '/workspace'...")]
    [InlineData("{not json")]
    [InlineData("""{"no_type":"object"}""")]
    [InlineData("")]
    public void WhenLineIsNotAStreamJsonEvent_ReturnsFalseAndLeavesLastEventAtUnset(string line)
    {
        // Act
        bool applied = _sut.Apply(line, ReceivedAt);

        // Assert
        applied.ShouldBeFalse();
        Snapshot().LastEventAt.ShouldBeNull();
    }

    [Fact]
    public void WhenThinkingTokensEventArrives_AdvancesLastEventAtFromDockerTimestamp()
    {
        // Arrange
        string line = """2026-11-02T10:15:30.123456789Z {"type":"system","subtype":"thinking_tokens","tokens":12}""";

        // Act
        bool applied = _sut.Apply(line, ReceivedAt);

        // Assert
        applied.ShouldBeTrue();
        Snapshot().LastEventAt.ShouldNotBeNull().ShouldBe(
            new DateTimeOffset(2026, 11, 2, 10, 15, 30, 123, TimeSpan.Zero),
            TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void WhenEventHasNoTimestamp_UsesReceivedAt()
    {
        // Act
        _sut.Apply("""{"type":"system","subtype":"hook_started"}""", ReceivedAt);

        // Assert
        Snapshot().LastEventAt.ShouldBe(ReceivedAt);
    }

    [Fact]
    public void WhenTaskCreateResultArrives_AddsTaskWithIdFromResultText()
    {
        // Arrange
        ApplyToolUse("c1", "TaskCreate", """{"subject":"Run tests","activeForm":"Running tests","description":"d"}""");

        // Act
        ApplyToolResult("c1", "Task #4 created successfully: Run tests");

        // Assert
        AgentTaskItem task = Snapshot().Tasks.ShouldHaveSingleItem();
        task.ShouldBe(new AgentTaskItem("4", "Run tests", "Running tests", "pending"));
    }

    [Fact]
    public void WhenTaskCreateResultIsStructured_UsesStructuredTaskId()
    {
        // Arrange
        ApplyToolUse("c1", "TaskCreate", """{"subject":"Plan"}""");

        // Act
        _sut.Apply(
            """{"type":"user","tool_use_result":{"task":{"id":"17","subject":"Plan"}},"message":{"content":[{"type":"tool_result","tool_use_id":"c1","content":"ok"}]}}""",
            ReceivedAt);

        // Assert
        Snapshot().Tasks.ShouldHaveSingleItem().Id.ShouldBe("17");
    }

    [Fact]
    public void WhenTaskUpdatesArrive_TracksCurrentTaskAndCompletedCount()
    {
        // Arrange
        CreateTask("c1", "1", "Explore", "Exploring");
        CreateTask("c2", "2", "Implement", "Implementing");
        CreateTask("c3", "3", "Test", "Running tests");

        // Act
        ApplyToolUse("u1", "TaskUpdate", """{"taskId":"1","status":"completed"}""");
        ApplyToolUse("u2", "TaskUpdate", """{"taskId":"2","status":"in_progress"}""");

        // Assert
        AgentActivity snapshot = Snapshot();
        snapshot.Tasks.Count.ShouldBe(3);
        snapshot.CompletedTaskCount.ShouldBe(1);
        snapshot.CurrentTask.ShouldNotBeNull().ActiveForm.ShouldBe("Implementing");
    }

    [Fact]
    public void WhenTaskUpdateDeletesTask_RemovesItFromTheList()
    {
        // Arrange
        CreateTask("c1", "1", "Explore", "Exploring");

        // Act
        ApplyToolUse("u1", "TaskUpdate", """{"taskId":"1","status":"deleted"}""");

        // Assert
        Snapshot().Tasks.ShouldBeEmpty();
    }

    [Fact]
    public void WhenToolUseHasNoResultYet_ItIsTheInFlightToolWithInputSummary()
    {
        // Act
        ApplyToolUse("t1", "Bash", """{"command":"dotnet test"}""");

        // Assert
        InFlightTool tool = Snapshot().InFlightTool.ShouldNotBeNull();
        tool.Name.ShouldBe("Bash");
        tool.Summary.ShouldBe("dotnet test");
    }

    [Fact]
    public void WhenToolResultArrives_ClearsTheInFlightTool()
    {
        // Arrange
        ApplyToolUse("t1", "Bash", """{"command":"dotnet test"}""");

        // Act
        ApplyToolResult("t1", "ok");

        // Assert
        Snapshot().InFlightTool.ShouldBeNull();
    }

    [Fact]
    public void WhenAgentAndSubagentToolArePending_InFlightToolIsTheSubagentTool()
    {
        // Arrange
        ApplyToolUse("a1", "Agent", """{"subagent_type":"developer","description":"Implement"}""");

        // Act
        ApplyToolUse("t1", "Read", """{"file_path":"/workspace/A.cs"}""", parentToolUseId: "a1");

        // Assert
        Snapshot().InFlightTool.ShouldNotBeNull().Name.ShouldBe("Read");
    }

    [Fact]
    public void WhenSkillIsCalled_RecordsActiveSkill()
    {
        // Act
        ApplyToolUse("s1", "Skill", """{"skill":"code-review","args":"high"}""");

        // Assert
        Snapshot().ActiveSkill.ShouldBe("code-review");
    }

    [Fact]
    public void WhenAgentIsCalled_TracksSubagentWithTypeDescriptionAndDepth()
    {
        // Act
        ApplyToolUse("a1", "Agent", """{"subagent_type":"developer","description":"Implement parser"}""");

        // Assert
        ActiveSubagent subagent = Snapshot().Subagents.ShouldHaveSingleItem();
        subagent.ShouldBe(new ActiveSubagent("developer", "Implement parser", 1, null));
    }

    [Fact]
    public void WhenSubagentRunsTools_RecordsLastTool()
    {
        // Arrange
        ApplyToolUse("a1", "Agent", """{"subagent_type":"developer","description":"Implement"}""");
        ApplySystem("""{"type":"system","subtype":"task_started","task_id":"t-1","tool_use_id":"a1","description":"Implement"}""");

        // Act
        ApplyToolUse("x1", "Grep", """{"pattern":"Foo"}""", parentToolUseId: "a1");
        ApplySystem("""{"type":"system","subtype":"task_progress","task_id":"t-1","last_tool_name":"Edit"}""");

        // Assert
        Snapshot().Subagents.ShouldHaveSingleItem().LastTool.ShouldBe("Edit");
    }

    [Fact]
    public void WhenParallelSubagentsRun_TracksThemSeparatelyAndRemovesOnCompletion()
    {
        // Arrange
        ApplyToolUse("a1", "Agent", """{"subagent_type":"Explore","description":"Find A"}""");
        ApplyToolUse("a2", "Agent", """{"subagent_type":"Explore","description":"Find B"}""");
        ApplySystem("""{"type":"system","subtype":"task_started","task_id":"t-1","tool_use_id":"a1"}""");
        ApplySystem("""{"type":"system","subtype":"task_started","task_id":"t-2","tool_use_id":"a2"}""");

        // Act
        ApplySystem("""{"type":"system","subtype":"task_notification","task_id":"t-1","status":"completed"}""");

        // Assert
        Snapshot().Subagents.ShouldHaveSingleItem().Description.ShouldBe("Find B");
    }

    [Fact]
    public void WhenSubagentSpawnsSubagent_NestedSubagentHasDepthTwo()
    {
        // Arrange
        ApplyToolUse("a1", "Agent", """{"subagent_type":"developer","description":"Implement"}""");

        // Act
        ApplyToolUse("a2", "Agent", """{"subagent_type":"Explore","description":"Look"}""", parentToolUseId: "a1");

        // Assert
        Snapshot().Subagents.Select(s => s.SpawnDepth).ShouldBe([1, 2]);
    }

    [Fact]
    public void WhenTaskStartedCarriesSpawnDepth_UsesIt()
    {
        // Act
        ApplySystem("""{"type":"system","subtype":"task_started","task_id":"t-9","subagent_type":"reviewer","description":"Review","spawn_depth":3}""");

        // Assert
        Snapshot().Subagents.ShouldHaveSingleItem().ShouldBe(new ActiveSubagent("reviewer", "Review", 3, null));
    }

    [Fact]
    public void WhenTaskStartedIsNotASubagent_IsIgnored()
    {
        // Act
        ApplySystem("""{"type":"system","subtype":"task_started","task_id":"b-1","description":"npm run dev"}""");

        // Assert
        Snapshot().Subagents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenAssistantMessageIsEmittedPerContentBlock_SumsBillableInputOncePerMessageId()
    {
        // Arrange
        const string usage = """{"input_tokens":10,"output_tokens":20,"cache_read_input_tokens":300,"cache_creation_input_tokens":40}""";

        // Act
        _sut.Apply($$$"""{"type":"assistant","message":{"id":"m1","usage":{{{usage}}},"content":[{"type":"text","text":"a"}]}}""", ReceivedAt);
        _sut.Apply($$$"""{"type":"assistant","message":{"id":"m1","usage":{{{usage}}},"content":[{"type":"text","text":"b"}]}}""", ReceivedAt);
        _sut.Apply($$$"""{"type":"assistant","message":{"id":"m2","usage":{{{usage}}},"content":[{"type":"text","text":"c"}]}}""", ReceivedAt);

        // Assert
        // Billable input = (10 + 300 + 40) per message, two distinct messages; streamed
        // output_tokens is a start-of-message stub, so output stays unknown while live.
        Snapshot().Tokens.ShouldBe(new AgentTokenTotals(700, null));
    }

    [Fact]
    public void WhenResultEventCarriesModelUsage_ReportsFinalOutputSummedAcrossModels()
    {
        // Arrange
        _sut.Apply("""{"type":"assistant","message":{"id":"m1","usage":{"input_tokens":1,"output_tokens":2,"cache_read_input_tokens":3,"cache_creation_input_tokens":4},"content":[{"type":"text","text":"a"}]}}""", ReceivedAt);

        // Act
        _sut.Apply("""{"type":"result","subtype":"success","modelUsage":{"claude-opus-5-5":{"inputTokens":1,"outputTokens":700,"cacheReadInputTokens":3,"cacheCreationInputTokens":4},"claude-haiku-5-5":{"inputTokens":5,"outputTokens":14,"cacheReadInputTokens":0,"cacheCreationInputTokens":0}}}""", ReceivedAt);

        // Assert
        Snapshot().Tokens.ShouldBe(new AgentTokenTotals(8, 714));
    }

    [Fact]
    public void WhenRunContinuesAfterResult_OutputIsHiddenAgain()
    {
        // Arrange — the claude -c transient retry resumes after a result event
        _sut.Apply("""{"type":"result","subtype":"error","modelUsage":{"claude-opus-5-5":{"inputTokens":1,"outputTokens":700,"cacheReadInputTokens":0,"cacheCreationInputTokens":0}}}""", ReceivedAt);

        // Act
        _sut.Apply("""{"type":"assistant","message":{"id":"m2","usage":{"input_tokens":1,"output_tokens":2,"cache_read_input_tokens":0,"cache_creation_input_tokens":0},"content":[{"type":"text","text":"a"}]}}""", ReceivedAt);

        // Assert
        Snapshot().Tokens.OutputTokens.ShouldBeNull();
    }

    [Fact]
    public void WhenSystemInitRepeats_ProjectionContinues()
    {
        // Arrange
        ApplySystem("""{"type":"system","subtype":"init","model":"claude"}""");
        CreateTask("c1", "1", "Explore", "Exploring");

        // Act
        ApplySystem("""{"type":"system","subtype":"init","model":"claude"}""");

        // Assert
        Snapshot().Tasks.ShouldHaveSingleItem();
    }

    [Fact]
    public void WhenResultArrives_ClearsPendingToolsAndSubagentsButKeepsTasks()
    {
        // Arrange
        CreateTask("c1", "1", "Explore", "Exploring");
        ApplyToolUse("a1", "Agent", """{"subagent_type":"developer"}""");
        ApplyToolUse("t1", "Bash", """{"command":"sleep 1"}""");

        // Act
        ApplySystem("""{"type":"result","subtype":"error_during_execution","is_error":true}""");

        // Assert
        AgentActivity snapshot = Snapshot();
        snapshot.InFlightTool.ShouldBeNull();
        snapshot.Subagents.ShouldBeEmpty();
        snapshot.Tasks.ShouldHaveSingleItem();
    }

    [Fact]
    public void WhenSummaryContainsSecret_RedactsIt()
    {
        // Act
        ApplyToolUse("t1", "Bash", """{"command":"git push https://x-access-token:ghp_abcdefghijklmnopqrstuvwxyz0123456789@github.com/o/r"}""");

        // Assert
        Snapshot().InFlightTool.ShouldNotBeNull().Summary.ShouldNotBeNull().ShouldNotContain("ghp_abcdefghijklmnopqrstuvwxyz0123456789");
    }

    private void CreateTask(string toolUseId, string taskId, string subject, string activeForm)
    {
        ApplyToolUse(toolUseId, "TaskCreate", $$"""{"subject":"{{subject}}","activeForm":"{{activeForm}}"}""");
        ApplyToolResult(toolUseId, $"Task #{taskId} created successfully: {subject}");
    }

    private void ApplyToolUse(string id, string name, string input, string? parentToolUseId = null)
    {
        string parent = parentToolUseId is null ? "null" : $"\"{parentToolUseId}\"";
        _sut.Apply(
            $$$"""{"type":"assistant","parent_tool_use_id":{{{parent}}},"message":{"content":[{"type":"tool_use","id":"{{{id}}}","name":"{{{name}}}","input":{{{input}}}}]}}""",
            ReceivedAt);
    }

    private void ApplyToolResult(string toolUseId, string content)
    {
        _sut.Apply(
            $$$"""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"{{{toolUseId}}}","content":"{{{content}}}"}]}}""",
            ReceivedAt);
    }

    private void ApplySystem(string line) => _sut.Apply(line, ReceivedAt);

    private AgentActivity Snapshot() => _sut.Snapshot(WorkerRunId.New(), Guid.NewGuid());
}
