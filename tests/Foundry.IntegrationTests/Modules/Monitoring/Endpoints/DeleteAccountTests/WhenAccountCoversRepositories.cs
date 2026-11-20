using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.DeleteAccountTests;

public sealed class WhenAccountCoversRepositories : IAsyncDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountCoversRepositories()
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
    public async Task ReturnsNoContent()
    {
        // Arrange
        (Guid accountId, _, _) = await SeedAccountWithTwoRepositoriesAsync();

        // Act
        HttpResponseMessage response = await DeleteAccountAsync(accountId);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemovesBothRepositoriesAndTheAccount()
    {
        // Arrange
        (Guid accountId, Guid first, Guid second) = await SeedAccountWithTwoRepositoriesAsync();

        // Act
        await DeleteAccountAsync(accountId);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        MonitoredRepositoryId[] ids = [MonitoredRepositoryId.From(first), MonitoredRepositoryId.From(second)];
        (await dbContext.Set<MonitoredRepository>().AnyAsync(r => ids.Contains(r.Id), CancellationToken.None)).ShouldBeFalse();
        CredentialId credentialId = CredentialId.From(accountId);
        (await dbContext.Set<Credential>().AnyAsync(c => c.Id == credentialId, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task DeletesTheQueuedIssueAndKeepsTheCompletedOne()
    {
        // Arrange
        (Guid accountId, Guid first, _) = await SeedAccountWithTwoRepositoriesAsync();
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.From(first);
        await SeedIssuesAsync(
            new IssueBuilder().WithMonitoredRepositoryId(repositoryId).WithIssueNumber(1).WithDetectedAt(BaseTime).FreshQueued(),
            new IssueBuilder().WithMonitoredRepositoryId(repositoryId).WithIssueNumber(2).WithDetectedAt(BaseTime).Completed());

        // Act
        await DeleteAccountAsync(accountId);
        await _factory.Services.GetRequiredService<OutboxRelayService>().TickForTest(TestContext.Current.CancellationToken);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        List<Issue> remaining = await dbContext.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == repositoryId)
            .ToListAsync(CancellationToken.None);
        remaining.ShouldHaveSingleItem().ShouldBeOfType<CompletedIssue>().IssueNumber.ShouldBe(2);
    }

    private async Task<(Guid AccountId, Guid First, Guid Second)> SeedAccountWithTwoRepositoriesAsync()
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        Guid first = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/one");
        Guid second = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/two");
        return (accountId, first, second);
    }

    private async Task SeedIssuesAsync(params Issue[] issues)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Set<Issue>().AddRange(issues);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private Task<HttpResponseMessage> DeleteAccountAsync(Guid accountId) =>
        _client.DeleteAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            TestContext.Current.CancellationToken);
}
