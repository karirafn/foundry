using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.DeleteAccountTests;

public sealed class WhenACoveredRepositoryHasWorkInFlight : IAsyncDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenACoveredRepositoryHasWorkInFlight()
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
        (Guid accountId, _) = await SeedAccountWithActiveRunAsync();

        // Act
        HttpResponseMessage response = await DeleteAccountAsync(accountId);

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
    public async Task KeepsTheRepositoryAndTheAccount()
    {
        // Arrange
        (Guid accountId, Guid repositoryId) = await SeedAccountWithActiveRunAsync();

        // Act
        await DeleteAccountAsync(accountId);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        MonitoredRepositoryId id = MonitoredRepositoryId.From(repositoryId);
        (await dbContext.Set<MonitoredRepository>().AnyAsync(r => r.Id == id, CancellationToken.None)).ShouldBeTrue();
        CredentialId credentialId = CredentialId.From(accountId);
        (await dbContext.Set<Credential>().AnyAsync(c => c.Id == credentialId, CancellationToken.None)).ShouldBeTrue();
    }

    private async Task<(Guid AccountId, Guid RepositoryId)> SeedAccountWithActiveRunAsync()
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId);

        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        MonitoredRepositoryId monitoredRepositoryId = MonitoredRepositoryId.From(repositoryId);
        WorkerRunId runId = WorkerRunId.New();
        InProgressIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(monitoredRepositoryId)
            .WithIssueNumber(42)
            .WithDetectedAt(BaseTime)
            .WithWorkerRunId(runId)
            .InProgress();
        dbContext.Set<Issue>().Add(issue);
        dbContext.Set<WorkerRun>().Add(StartingRun.Begin(issue.Id, runId)
            .Activate(ContainerId.From("container-1"), BranchName.From("feat/42-run"), monitoredRepositoryId));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (accountId, repositoryId);
    }

    private Task<HttpResponseMessage> DeleteAccountAsync(Guid accountId) =>
        _client.DeleteAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            TestContext.Current.CancellationToken);
}
