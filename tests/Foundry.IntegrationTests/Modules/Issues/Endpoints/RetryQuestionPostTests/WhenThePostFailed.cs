using System.Net;
using System.Text.Json.Nodes;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.RetryQuestionPostTests;

public sealed class WhenThePostFailed : IAsyncDisposable
{
    private static readonly DateTimeOffset PostedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenThePostFailed()
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
            .WithUrl(ProviderUrl.Create("https://github.com/owner/repo/issues/2").ValueOrThrow())
            .WithLabels([]);

    private async Task<Issue> SeedAsync(Issue issue)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Set<Issue>().Add(issue);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return issue;
    }

    private async Task<Issue> ReloadAsync(Issue issue)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        return await dbContext.Set<Issue>().SingleAsync(i => i.Id == issue.Id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsTheDetailAndSendsThePostBackToPending()
    {
        // Arrange
        AwaitingInputIssue awaiting = Builder().AwaitingInput();
        awaiting.RecordQuestionPostFailed("404", transient: false, PostedAt);
        await SeedAsync(awaiting);

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/issues/{awaiting.Id.Value}/retry-post", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReloadAsync(awaiting)).ShouldBeOfType<AwaitingInputIssue>().QuestionPost.ShouldBeOfType<QuestionPost.Pending>();
    }
}
