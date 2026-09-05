using System.Net;

using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.RetryIssueTests;

public sealed class WhenIssueIsInNonRetryableState : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenIssueIsInNonRetryableState()
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

    private async Task<InProgressIssue> SeedInProgressIssueAsync()
    {
        // No POST endpoint exists for issues — they are created via integration events.
        // Seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(2)
            .WithTitle("An in-progress issue")
            .WithUrl(ProviderUrl.Create("https://github.com/owner/repo/issues/2").ValueOrThrow())
            .WithLabels([])
            .InProgress();

        dbContext.Set<Issue>().Add(inProgress);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return inProgress;
    }

    [Fact]
    public async Task ReturnsConflict()
    {
        // Arrange
        InProgressIssue inProgress = await SeedInProgressIssueAsync();

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/issues/{inProgress.Id.Value}/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task StateIsUnchanged()
    {
        // Arrange
        InProgressIssue inProgress = await SeedInProgressIssueAsync();

        // Act
        await _client.PostAsync(
            new Uri($"/api/issues/{inProgress.Id.Value}/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(
                i => i.Id == inProgress.Id,
                TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<InProgressIssue>();
    }
}
