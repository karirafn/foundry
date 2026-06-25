using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.RetryIssueTests;

public sealed class WhenIssueIsInFailedState : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    public WhenIssueIsInFailedState()
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

    private async Task<FailedIssue> SeedFailedIssueAsync()
    {
        // No POST endpoint exists for issues — they are created via integration events.
        // Seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "A failed issue",
            body: "Issue body text",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["bug"],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = QueuedIssue.FromDetected(detected);
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        FailedIssue failed = inProgress.MarkFailed(
            Guid.NewGuid(),
            "Container exited with non-zero code: 1",
            DateTimeOffset.UtcNow);

        dbContext.Set<Issue>().Add(failed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return failed;
    }

    [Fact]
    public async Task ReturnsIssueDetailWithQueuedState()
    {
        // Arrange
        FailedIssue failed = await SeedFailedIssueAsync();

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/issues/{failed.Id.Value}/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueDetail? detail = await response.Content.ReadFromJsonAsync<IssueDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();
        detail.ShouldSatisfyAllConditions(
            () => detail.Id.ShouldBe(failed.Id.Value),
            () => detail.State.ShouldBe("queued"));
    }

    [Fact]
    public async Task PersistsTransitionToQueuedState()
    {
        // Arrange
        FailedIssue failed = await SeedFailedIssueAsync();

        // Act
        await _client.PostAsync(
            new Uri($"/api/issues/{failed.Id.Value}/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == failed.Id,
                TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<QueuedIssue>();
    }
}
