using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UnclaimedRepositoryTests;

public sealed class WhenRepositoryIsUnclaimed : IAsyncDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRepositoryIsUnclaimed()
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
    public async Task ListsOnlyTheUnclaimedRepository()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: "owner/covered");
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        Guid unclaimedId = await SeedUnclaimedRepositoryAsync("orphan/project");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/repositories/unclaimed", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<UnclaimedRepositorySummary> unclaimed = (await response.Content
            .ReadFromJsonAsync<IReadOnlyList<UnclaimedRepositorySummary>>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        UnclaimedRepositorySummary summary = unclaimed.ShouldHaveSingleItem();
        summary.Id.ShouldBe(unclaimedId);
        summary.Host.ShouldBe("gitlab.example.com");
        summary.Slug.ShouldBe("orphan/project");
        summary.Violation.ShouldBe("no-credential:orphan");
        summary.ViolationDescription.ShouldBe(EligibilityViolationInfo.NoCredentialDescription("orphan"));
    }

    [Fact]
    public async Task DeleteRemovesItFromTheUnclaimedList()
    {
        // Arrange
        Guid unclaimedId = await SeedUnclaimedRepositoryAsync("orphan/project");

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/repositories/{unclaimedId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        HttpResponseMessage list = await _client.GetAsync(
            new Uri("/api/repositories/unclaimed", UriKind.Relative),
            TestContext.Current.CancellationToken);
        IReadOnlyList<UnclaimedRepositorySummary> unclaimed = (await list.Content
            .ReadFromJsonAsync<IReadOnlyList<UnclaimedRepositorySummary>>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        unclaimed.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteTwiceReturnsNotFound()
    {
        // Arrange
        Guid unclaimedId = await SeedUnclaimedRepositoryAsync("orphan/project");
        await _client.DeleteAsync(
            new Uri($"/api/repositories/{unclaimedId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/repositories/{unclaimedId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteUnknownIdReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/repositories/{Guid.NewGuid()}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteWithWorkInFlightReturnsConflict()
    {
        // Arrange
        Guid unclaimedId = await SeedUnclaimedRepositoryAsync("orphan/project");
        await SeedInProgressIssueAsync(unclaimedId, 42);

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/repositories/{unclaimedId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RecheckOnAHostWithNoAccountReturnsZeroReclaimed()
    {
        // Arrange
        await SeedUnclaimedRepositoryAsync("orphan/project");

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri("/api/repositories/unclaimed/recheck?host=gitlab.example.com", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        RecheckClaimsResponse body = (await response.Content
            .ReadFromJsonAsync<RecheckClaimsResponse>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        body.ReclaimedRepositoryCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("/api/repositories/unclaimed/recheck")]
    [InlineData("/api/repositories/unclaimed/recheck?host=")]
    public async Task RecheckWithoutAHostReturnsBadRequest(string path)
    {
        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri(path, UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    private async Task<Guid> SeedUnclaimedRepositoryAsync(string slug)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        int position = await dbContext.Set<MonitoredRepository>().CountAsync(CancellationToken.None);
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            "gitlab.example.com",
            null,
            position).ValueOrThrow();
        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repository.Id.Value;
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
