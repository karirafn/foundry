using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.RetryIssueTests;

public sealed class WhenIssueIsInUnchangedState : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/5").ValueOrThrow();

    public WhenIssueIsInUnchangedState()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        // FoundryWebAppFactory creates an isolated in-memory SQLite connection per instance.
        // Each test class instance gets its own factory, so no explicit DELETE cleanup is needed.
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<UnchangedIssue> SeedUnchangedIssueAsync()
    {
        // No POST endpoint exists for issues — they are created via integration events.
        // Seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 5,
            title: "An unchanged issue",
            body: "Issue body text",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
        FreshQueuedIssue queued = FreshQueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        UnchangedIssue unchanged = inProgress.MarkUnchanged(Guid.NewGuid());

        dbContext.Set<Issue>().Add(unchanged);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return unchanged;
    }

    [Fact]
    public async Task ReturnsIssueDetailWithQueuedState()
    {
        // Arrange
        UnchangedIssue unchanged = await SeedUnchangedIssueAsync();

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/issues/{unchanged.Id.Value}/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueDetail? detail = await response.Content.ReadFromJsonAsync<IssueDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();
        detail.ShouldSatisfyAllConditions(
            () => detail.Id.ShouldBe(unchanged.Id.Value),
            () => detail.State.ShouldBe("queued"));
    }

    [Fact]
    public async Task PersistsTransitionToQueuedState()
    {
        // Arrange
        UnchangedIssue unchanged = await SeedUnchangedIssueAsync();

        // Act
        await _client.PostAsync(
            new Uri($"/api/issues/{unchanged.Id.Value}/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == unchanged.Id,
                TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<FreshQueuedIssue>();
    }
}
