using System.Net;
using System.Text.Json.Nodes;

using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.RetryIssueTests;

public sealed class WhenIssueIsInBranchDivergedState : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenIssueIsInBranchDivergedState()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<BranchDivergedIssue> SeedBranchDivergedIssueAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        BranchDivergedIssue diverged = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(42)
            .WithUrl(ProviderUrl.Create("https://github.com/owner/repo/issues/42").ValueOrThrow())
            .WithBranchName("feat/42-add-retry")
            .BranchDiverged("feat/42-add-retry-run-abc");

        dbContext.Set<Issue>().Add(diverged);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return diverged;
    }

    [Fact]
    public async Task DetailCarriesBothBranchesAndTheirUrls()
    {
        // Arrange
        BranchDivergedIssue diverged = await SeedBranchDivergedIssueAsync();

        // Act
        string body = await _client.GetStringAsync(
            new Uri($"/api/issues/{diverged.Id.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        JsonNode node = JsonNode.Parse(body).ShouldNotBeNull();
        JsonNode stateDetails = node["stateDetails"].ShouldNotBeNull();
        node.ShouldSatisfyAllConditions(
            () => node["state"]!.GetValue<string>().ShouldBe("branch_diverged"),
            () => stateDetails["branchName"]!.GetValue<string>().ShouldBe("feat/42-add-retry"),
            () => stateDetails["branchUrl"]!.GetValue<string>().ShouldBe("https://github.com/owner/repo/tree/feat/42-add-retry"),
            () => stateDetails["sideBranchName"]!.GetValue<string>().ShouldBe("feat/42-add-retry-run-abc"),
            () => stateDetails["sideBranchUrl"]!.GetValue<string>().ShouldBe("https://github.com/owner/repo/tree/feat/42-add-retry-run-abc"));
    }

    [Fact]
    public async Task RetryQueuesAContinuationOnTheIssueBranch()
    {
        // Arrange
        BranchDivergedIssue diverged = await SeedBranchDivergedIssueAsync();

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/issues/{diverged.Id.Value}/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Issue persisted = await dbContext.Set<Issue>()
            .SingleAsync(i => i.Id == diverged.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<ContinuationQueuedIssue>().BranchName.ShouldBe("feat/42-add-retry");
    }
}
