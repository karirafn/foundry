using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.ValidateTokenTests;

public sealed class WhenScopesCannotBeVerified : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenScopesCannotBeVerified()
    {
        ValidateToken.Response scopesUnverifiableResponse = new(
            Kind: ValidateToken.Kinds.ScopesUnverifiable,
            AccountName: null,
            MissingScopes: [],
            DetectedProvider: null);
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubHandler(Result<ValidateToken.Response>.Ok(scopesUnverifiableResponse)));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsOkWithScopesUnverifiableKind()
    {
        // Arrange
        object body = new { token = "glpat_unverifiable", baseUrl = "https://gitlab.com", providerType = "gitlab" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ValidateToken.Response? dto = await response.Content
            .ReadFromJsonAsync<ValidateToken.Response>(TestContext.Current.CancellationToken);
        dto.ShouldNotBeNull();
        dto.ShouldSatisfyAllConditions(
            () => dto.Kind.ShouldBe(ValidateToken.Kinds.ScopesUnverifiable),
            () => dto.MissingScopes.ShouldBeEmpty());
    }

    private sealed class StubHandler(Result<ValidateToken.Response> result)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
