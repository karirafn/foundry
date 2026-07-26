using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetAvailableRepositoriesTests;

public sealed class WhenAccountHasToken : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountHasToken()
    {
        AvailableRepositoriesResponse fakeResponse = new(
            HasClaims: true,
            Repositories:
            [
                new AvailableRepository("owner/repo-a", IsPrivate: false, CanPush: true, IsMonitored: false),
                new AvailableRepository("owner/repo-b", IsPrivate: true, CanPush: false, IsMonitored: false),
            ]);

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<GetAvailableRepositories.Query, AvailableRepositoriesResponse>>();
            services.AddScoped<IQueryHandler<GetAvailableRepositories.Query, AvailableRepositoriesResponse>>(
                _ => new StubHandler(Result<AvailableRepositoriesResponse>.Ok(fakeResponse)));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsAvailableRepositories()
    {
        // Arrange
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/accounts/{accountId}/repositories/available-repositories", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AvailableRepositoriesResponse? result = await response.Content
            .ReadFromJsonAsync<AvailableRepositoriesResponse>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Repositories.Count.ShouldBe(2);
        result.Repositories.ShouldSatisfyAllConditions(
            () => result.Repositories.ShouldContain(r => r.Slug == "owner/repo-a" && !r.IsPrivate && r.CanPush),
            () => result.Repositories.ShouldContain(r => r.Slug == "owner/repo-b" && r.IsPrivate && !r.CanPush));
    }

    private sealed class StubHandler(Result<AvailableRepositoriesResponse> result)
        : IQueryHandler<GetAvailableRepositories.Query, AvailableRepositoriesResponse>
    {
        public Task<Result<AvailableRepositoriesResponse>> HandleAsync(
            GetAvailableRepositories.Query query,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
