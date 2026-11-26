using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.ContainerOutputParserTests;

public sealed class ParsePlanUsage
{
    // The rate_limit_info shape the CLI emits on a subscription account (CLI 2.1.302+, as in
    // ProbeTurnUsageParserTests.Parse.FullRateLimitEventLog), behind the Docker timestamp a container log line carries.
    private const string StartEvent =
        """2026-11-16T10:00:01.000000000Z {"type":"rate_limit_event","rate_limit_info":{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.54,"resetsAt":1793399400,"status":"allowed"},"seven_day":{"utilization":0.47,"resetsAt":1793527200,"status":"allowed"}}}}""";

    private const string EndEvent =
        """2026-11-16T10:40:00.000000000Z {"type":"rate_limit_event","rate_limit_info":{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.6,"resetsAt":1793399400,"status":"allowed"},"seven_day":{"utilization":0.5,"resetsAt":1793527200,"status":"allowed"}}}}""";

    private const string Result =
        """2026-11-16T10:41:00.000000000Z {"type":"result","subtype":"success","is_error":false}""";

    private readonly IContainerOutputParser _sut = new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance, new PlanUsageWindowReader());

    [Fact]
    public void WhenTheLogHoldsTimestampedEvents_ReturnsTheFirstAndTheLastMeters()
    {
        // Arrange
        string log = "2026-11-16T10:00:00.000000000Z Worker role: implement\n" + StartEvent + "\n" + EndEvent + "\n" + Result;

        // Act
        RunPlanUsage? usage = _sut.ParsePlanUsage(log);

        // Assert
        usage.ShouldBe(new RunPlanUsage(new PlanUsage(0.54m, 0.47m), new PlanUsage(0.6m, 0.5m)));
    }

    [Fact]
    public void WhenTheFirstEventIsBeyondTheParsedTail_StillReadsIt()
    {
        // Arrange — the first event is written early; a long run's output pushes it out of the 64 KB tail.
        string longOutput = string.Join('\n', Enumerable.Repeat(
            """2026-11-16T10:00:02.000000000Z {"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}""",
            2_000));
        string log = StartEvent + "\n" + longOutput + "\n" + EndEvent;

        // Act
        RunPlanUsage? usage = _sut.ParsePlanUsage(log);

        // Assert
        usage.ShouldNotBeNull().AtStart.ShouldBe(new PlanUsage(0.54m, 0.47m));
    }

    [Fact]
    public void WhenTheLogHoldsNoEvent_ReturnsNull()
    {
        // Act
        RunPlanUsage? usage = _sut.ParsePlanUsage(Result);

        // Assert
        usage.ShouldBeNull();
    }

    [Fact]
    public void WhenTheLogIsEmpty_ReturnsNull()
    {
        // Act
        RunPlanUsage? usage = _sut.ParsePlanUsage(null);

        // Assert
        usage.ShouldBeNull();
    }
}
