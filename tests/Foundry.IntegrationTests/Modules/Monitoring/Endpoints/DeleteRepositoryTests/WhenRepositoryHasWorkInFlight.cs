using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Testing;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.DeleteRepositoryTests;

public sealed class WhenRepositoryHasWorkInFlight : IAsyncDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRepositoryHasWorkInFlight()
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
    public async Task ReturnsConflictNamingTheBlockingIssues()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId);
        await SeedInProgressIssueAsync(repositoryId, 42);

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken)).ShouldNotBeNull();
        problem.Detail.ShouldNotBeNull().ShouldContain("owner/repo#42 (in_progress)");
        JsonElement blocking = problem.Extensions["blockingIssues"].ShouldBeOfType<JsonElement>();
        JsonElement issue = blocking.EnumerateArray().ShouldHaveSingleItem();
        issue.GetProperty("repositorySlug").GetString().ShouldBe("owner/repo");
        issue.GetProperty("issueNumber").GetInt32().ShouldBe(42);
        issue.GetProperty("state").GetString().ShouldBe("in_progress");
    }

    [Fact]
    public async Task KeepsTheRepository()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId);
        await SeedInProgressIssueAsync(repositoryId, 42);

        // Act
        await _client.DeleteAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        MonitoredRepositoryId id = MonitoredRepositoryId.From(repositoryId);
        (await dbContext.Set<MonitoredRepository>().AnyAsync(r => r.Id == id, CancellationToken.None)).ShouldBeTrue();
    }

    private async Task SeedInProgressIssueAsync(Guid repositoryId, int issueNumber)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        dbContext.Set<Issue>().Add(new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.From(repositoryId))
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(BaseTime)
            .InProgress());
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
