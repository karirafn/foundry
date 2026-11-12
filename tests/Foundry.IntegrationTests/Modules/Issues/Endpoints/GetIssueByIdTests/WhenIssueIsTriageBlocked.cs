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

public sealed class WhenIssueIsTriageBlocked : IAsyncDisposable
{
    private const string BlockerReason = "Needs the retry queue.";

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenIssueIsTriageBlocked()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenRequested_ReturnsTheBlockersWithReasonsAndTheBlockedOutcome()
    {
        // Arrange
        TriageVerdict.Blocked verdict = new(IssueKind.Feature, [new TriageBlocker(3, BlockerReason)], DateTimeOffset.UtcNow);
        TriageBlockedIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .TriageBlockedFromTriage(verdict);
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            dbContext.Set<Issue>().Add(issue);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues/{issue.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement blocker = body.RootElement.GetProperty("stateDetails").GetProperty("blockedBy")[0];
        body.RootElement.ShouldSatisfyAllConditions(
            () => blocker.GetProperty("issueNumber").GetInt32().ShouldBe(3),
            () => blocker.GetProperty("reason").GetString().ShouldBe(BlockerReason),
            () => body.RootElement.GetProperty("triage").GetProperty("outcome").GetString().ShouldBe("blocked"));
    }
}
