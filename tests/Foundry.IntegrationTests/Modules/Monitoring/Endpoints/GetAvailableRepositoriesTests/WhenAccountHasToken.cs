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
        IReadOnlyList<AvailableRepository> fakeRepositories =
        [
            new AvailableRepository("owner/repo-a", IsPrivate: false),
            new AvailableRepository("owner/repo-b", IsPrivate: true),
        ];

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<GetAvailableRepositories.Query, IReadOnlyList<AvailableRepository>>>();
            services.AddScoped<IQueryHandler<GetAvailableRepositories.Query, IReadOnlyList<AvailableRepository>>>(
                _ => new StubHandler(Result<IReadOnlyList<AvailableRepository>>.Ok(fakeRepositories)));
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
        IReadOnlyList<AvailableRepository>? repositories = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<AvailableRepository>>(TestContext.Current.CancellationToken);
        repositories.ShouldNotBeNull();
        repositories.Count.ShouldBe(2);
        repositories.ShouldSatisfyAllConditions(
            () => repositories.ShouldContain(r => r.Slug == "owner/repo-a" && !r.IsPrivate),
            () => repositories.ShouldContain(r => r.Slug == "owner/repo-b" && r.IsPrivate));
    }

    private sealed class StubHandler(Result<IReadOnlyList<AvailableRepository>> result)
        : IQueryHandler<GetAvailableRepositories.Query, IReadOnlyList<AvailableRepository>>
    {
        public Task<Result<IReadOnlyList<AvailableRepository>>> HandleAsync(
            GetAvailableRepositories.Query query,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
