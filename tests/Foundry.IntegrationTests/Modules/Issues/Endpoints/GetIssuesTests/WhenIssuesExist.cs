using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenIssuesExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    public WhenIssuesExist()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedIssueAsync(int issueNumber, string title)
    {
        // No POST endpoint exists for issues — they are created via integration events.
        // Seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle(title)
            .WithBody("Body")
            .WithLabels([])
            .Detected();

        dbContext.Set<Issue>().Add(issue);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsOkWithSummaries()
    {
        // Arrange
        await SeedIssueAsync(issueNumber: 1, title: "First issue");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<IssueSummary>? summaries = await response.Content.ReadFromJsonAsync<IReadOnlyList<IssueSummary>>(
            TestContext.Current.CancellationToken);
        summaries.ShouldNotBeNull();
        IssueSummary summary = summaries.ShouldHaveSingleItem();
        summary.ShouldSatisfyAllConditions(
            () => summary.IssueNumber.ShouldBe(1),
            () => summary.Title.ShouldBe("First issue"),
            () => summary.State.ShouldBe("detected"));
    }
}
