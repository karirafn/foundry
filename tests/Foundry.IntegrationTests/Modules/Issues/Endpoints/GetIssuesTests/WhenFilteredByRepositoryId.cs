using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenFilteredByRepositoryId : IAsyncLifetime
{
    private readonly FoundryWebAppFactory _factory;
    private HttpClient _client = null!;

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    public WhenFilteredByRepositoryId()
    {
        _factory = new FoundryWebAppFactory();
    }

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        // No POST endpoint exists for issues — they are created via integration events.
        // Seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        dbContext.Set<Issue>().Add(issue);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsOnlyIssuesForSpecifiedRepository()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();

        await SeedIssueAsync(targetRepo, issueNumber: 1);
        await SeedIssueAsync(otherRepo, issueNumber: 2);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues?repositoryId={targetRepo.Value}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<IssueSummary>? summaries = await response.Content.ReadFromJsonAsync<IReadOnlyList<IssueSummary>>(
            TestContext.Current.CancellationToken);
        summaries.ShouldNotBeNull();
        IssueSummary summary = summaries.ShouldHaveSingleItem();
        summary.IssueNumber.ShouldBe(1);
    }
}
