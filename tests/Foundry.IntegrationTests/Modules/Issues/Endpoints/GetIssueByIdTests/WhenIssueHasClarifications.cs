using System.Net;
using System.Text.Json;

using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssueByIdTests;

public sealed class WhenIssueHasClarifications : IAsyncDisposable
{
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AnsweredAt = PostedAt.AddHours(2);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenIssueHasClarifications()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static IssueBuilder Builder() =>
        new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(2)
            .WithUrl(ProviderUrl.Create("https://github.com/owner/repo/issues/2").ValueOrThrow());

    private async Task SeedAsync(Issue issue)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Set<Issue>().Add(issue);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<JsonDocument> GetDetailAsync(Issue issue)
    {
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues/{issue.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhenARoundClosed_ReturnsItsQuestionsTimesAndTypedInputParts()
    {
        // Arrange
        AwaitingInputIssue awaiting = Builder().AwaitingInputPosted(PostedAt);
        ClarificationInput input = new(
        [
            new InputPart.Comments("Use <b>Postgres</b>."),
            new InputPart.DescriptionEdited(),
            new InputPart.BranchUpdated("aaa111", "bbb222"),
            new InputPart.BranchUpdated(FromSha: null, "ccc333"),
        ]);
        Issue resumed = awaiting.ProvideInput(input, AnsweredAt, AnsweredAt);
        await SeedAsync(resumed);

        // Act
        using JsonDocument body = await GetDetailAsync(resumed);

        // Assert
        JsonElement round = body.RootElement.GetProperty("clarifications").EnumerateArray().ShouldHaveSingleItem();
        JsonElement[] parts = [.. round.GetProperty("input").EnumerateArray()];
        round.ShouldSatisfyAllConditions(
            () => round.GetProperty("questions")[0].GetProperty("question").GetString().ShouldBe("Which database?"),
            () => round.GetProperty("askedAt").GetDateTimeOffset().ShouldNotBe(default),
            () => round.GetProperty("answeredAt").GetDateTimeOffset().ShouldBe(AnsweredAt),
            () => parts.Length.ShouldBe(4),
            () => parts[0].GetProperty("kind").GetString().ShouldBe("comments"),
            () => parts[0].GetProperty("text").GetString().ShouldBe("Use <b>Postgres</b>."),
            () => parts[1].GetProperty("kind").GetString().ShouldBe("descriptionEdited"),
            () => parts[2].GetProperty("kind").GetString().ShouldBe("branchUpdated"),
            () => parts[2].GetProperty("fromSha").GetString().ShouldBe("aaa111"),
            () => parts[2].GetProperty("toSha").GetString().ShouldBe("bbb222"),
            () => parts[2].GetProperty("compareUrl").GetString().ShouldBe("https://github.com/owner/repo/compare/aaa111...bbb222"),
            () => parts[3].GetProperty("compareUrl").GetString().ShouldBe("https://github.com/owner/repo/commit/ccc333"));
    }

    [Fact]
    public async Task WhenContinuedWithoutAnswer_ReturnsTheContinuedPart()
    {
        // Arrange
        AwaitingInputIssue awaiting = Builder().AwaitingInputPosted(PostedAt);
        Issue resumed = awaiting.ContinueWithoutAnswer(AnsweredAt);
        await SeedAsync(resumed);

        // Act
        using JsonDocument body = await GetDetailAsync(resumed);

        // Assert
        JsonElement round = body.RootElement.GetProperty("clarifications").EnumerateArray().ShouldHaveSingleItem();
        round.GetProperty("input").EnumerateArray().ShouldHaveSingleItem().GetProperty("kind").GetString().ShouldBe("continuedWithoutAnswer");
    }

    [Fact]
    public async Task WhenARoundIsOpen_ReturnsItWithoutInputAndTheReminderDueTime()
    {
        // Arrange
        AwaitingInputIssue awaiting = Builder().AwaitingInputPosted(PostedAt);
        await SeedAsync(awaiting);

        // Act
        using JsonDocument body = await GetDetailAsync(awaiting);

        // Assert
        JsonElement round = body.RootElement.GetProperty("clarifications").EnumerateArray().ShouldHaveSingleItem();
        JsonElement details = body.RootElement.GetProperty("stateDetails").GetProperty("awaitingInput");
        body.RootElement.ShouldSatisfyAllConditions(
            () => round.GetProperty("input").ValueKind.ShouldBe(JsonValueKind.Null),
            () => round.GetProperty("answeredAt").ValueKind.ShouldBe(JsonValueKind.Null),
            // The reminder delay defaults to 72 hours.
            () => details.GetProperty("reminderDueAt").GetDateTimeOffset().ShouldBe(PostedAt.AddHours(72)));
    }

    [Fact]
    public async Task WhenTheReminderWasSent_ReturnsNoReminderDueTime()
    {
        // Arrange
        AwaitingInputIssue awaiting = Builder().AwaitingInputPosted(PostedAt);
        awaiting.RecordReminderPosted(commentId: 1002, PostedAt.AddHours(72));
        await SeedAsync(awaiting);

        // Act
        using JsonDocument body = await GetDetailAsync(awaiting);

        // Assert
        JsonElement details = body.RootElement.GetProperty("stateDetails").GetProperty("awaitingInput");
        details.GetProperty("reminderDueAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task WhenTriageDecidedWithoutAsking_ReturnsTheDecisionsWithTheirGround()
    {
        // Arrange
        TriageDecision decision = TriageDecisionBuilder.Build(ground: TriageDecisionGround.SettledBusiness);
        Issue queued = Builder().TriageInProgress()
            .MarkReady(new TriageVerdict.Ready(IssueKind.Bug, PostedAt), [decision], PostedAt);
        await SeedAsync(queued);

        // Act
        using JsonDocument body = await GetDetailAsync(queued);

        // Assert
        JsonElement round = body.RootElement.GetProperty("clarifications").EnumerateArray().ShouldHaveSingleItem();
        JsonElement decided = round.GetProperty("decisions").EnumerateArray().ShouldHaveSingleItem();
        round.ShouldSatisfyAllConditions(
            () => round.GetProperty("questions").GetArrayLength().ShouldBe(0),
            () => round.GetProperty("askedAt").GetDateTimeOffset().ShouldBe(PostedAt),
            () => decided.GetProperty("question").GetString().ShouldBe(decision.Question),
            () => decided.GetProperty("decision").GetString().ShouldBe(decision.Decision),
            () => decided.GetProperty("ground").GetString().ShouldBe("settled_business"),
            () => decided.GetProperty("evidence")[0].GetString().ShouldBe(decision.Evidence[0]));
    }

    [Fact]
    public async Task WhenNoRunAsked_ReturnsAnEmptyLog()
    {
        // Arrange
        DetectedIssue issue = Builder().Detected();
        await SeedAsync(issue);

        // Act
        using JsonDocument body = await GetDetailAsync(issue);

        // Assert
        body.RootElement.GetProperty("clarifications").GetArrayLength().ShouldBe(0);
    }
}
