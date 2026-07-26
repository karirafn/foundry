using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Infrastructure;
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
        IReadOnlyList<ProviderRepository> fakeRepositories =
        [
            new ProviderRepository("owner/repo-a", IsPrivate: false, CanPush: true),
            new ProviderRepository("owner/repo-b", IsPrivate: true, CanPush: false),
        ];

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<GetAvailableRepositories.Query, IReadOnlyList<ProviderRepository>>>();
            services.AddScoped<IQueryHandler<GetAvailableRepositories.Query, IReadOnlyList<ProviderRepository>>>(
                _ => new StubHandler(Result<IReadOnlyList<ProviderRepository>>.Ok(fakeRepositories)));
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
        IReadOnlyList<ProviderRepository>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<ProviderRepository>>(TestContext.Current.CancellationToken);
        repositories.ShouldNotBeNull();
        repositories.Count.ShouldBe(2);
        repositories.ShouldSatisfyAllConditions(
            () => repositories.ShouldContain(r => r.Slug == "owner/repo-a" && !r.IsPrivate && r.CanPush),
            () => repositories.ShouldContain(r => r.Slug == "owner/repo-b" && r.IsPrivate && !r.CanPush));
    }

    private sealed class StubHandler(Result<IReadOnlyList<ProviderRepository>> result)
        : IQueryHandler<GetAvailableRepositories.Query, IReadOnlyList<ProviderRepository>>
    {
        public Task<Result<IReadOnlyList<ProviderRepository>>> HandleAsync(
            GetAvailableRepositories.Query query,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
