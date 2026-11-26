using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Features.Usage;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.PlanUsageWindowReaderTests;

public sealed class Read
{
    // The rate_limit_info shape the CLI emits on a subscription account (CLI 2.1.302+), as captured in
    // ProbeTurnUsageParserTests.Parse.FullRateLimitEventLog and the issue's sample event.
    private const string StartEvent =
        """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.54,"resetsAt":1793399400,"status":"allowed"},"seven_day":{"utilization":0.47,"resetsAt":1793527200,"status":"allowed"}}},"uuid":"a","session_id":"s"}""";

    private const string EndEvent =
        """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.61,"resetsAt":1793399400,"status":"allowed"},"seven_day":{"utilization":0.49,"resetsAt":1793527200,"status":"allowed"}}},"uuid":"b","session_id":"s"}""";

    private const string EndEventWithoutSevenDay =
        """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.61,"resetsAt":1793399400,"status":"allowed"}}},"uuid":"b","session_id":"s"}""";

    private const string EventWithoutUnifiedWindows =
        """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":1793527200,"rateLimitType":"seven_day"},"uuid":"p","session_id":"s"}""";

    private const string Init = """{"type":"system","subtype":"init","session_id":"s"}""";

    private const string Result = """{"type":"result","subtype":"success","is_error":false,"result":"ok","session_id":"s"}""";

    private readonly IPlanUsageWindowReader _sut = new PlanUsageWindowReader();

    [Fact]
    public void WhenSeveralEventsCarryWindows_ReturnsTheFirstAndTheLast()
    {
        PlanUsageWindows windows = _sut.Read([Init, StartEvent, "plain stderr notice", EndEvent, Result]);

        windows.First.ShouldBe(new PlanUsageMeters(0.54m, 0.47m));
        windows.Last.ShouldBe(new PlanUsageMeters(0.61m, 0.49m));
    }

    [Fact]
    public void WhenOneEventCarriesWindows_FirstEqualsLast()
    {
        PlanUsageWindows windows = _sut.Read([Init, StartEvent, Result]);

        windows.First.ShouldBe(new PlanUsageMeters(0.54m, 0.47m));
        windows.Last.ShouldBe(windows.First);
    }

    [Fact]
    public void WhenNoEventCarriesWindows_ReturnsNoMeters()
    {
        PlanUsageWindows windows = _sut.Read([Init, EventWithoutUnifiedWindows, Result]);

        windows.ShouldBe(PlanUsageWindows.None);
    }

    [Fact]
    public void WhenTheLastEventOmitsSevenDay_OnlyThatMeterIsNull()
    {
        PlanUsageWindows windows = _sut.Read([StartEvent, EndEventWithoutSevenDay]);

        windows.First.ShouldBe(new PlanUsageMeters(0.54m, 0.47m));
        windows.Last.ShouldBe(new PlanUsageMeters(0.61m, null));
    }

    [Fact]
    public void WhenAWindowsUtilizationIsNotANumber_OnlyThatMeterIsNull()
    {
        const string malformedFiveHour =
            """{"type":"rate_limit_event","rate_limit_info":{"unifiedWindows":{"five_hour":{"utilization":"high"},"seven_day":{"utilization":0.47}}}}""";

        PlanUsageWindows windows = _sut.Read([malformedFiveHour]);

        windows.Last.ShouldBe(new PlanUsageMeters(null, 0.47m));
    }

    [Fact]
    public void WhenAnEventIsMalformed_ItIsSkipped()
    {
        const string malformedEvent = """{"type":"rate_limit_event","rate_limit_info":"broken"}""";

        PlanUsageWindows windows = _sut.Read([StartEvent, malformedEvent, "{not json"]);

        windows.Last.ShouldBe(new PlanUsageMeters(0.54m, 0.47m));
    }
}
