using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.ValidateTokenTests;

public sealed class WhenTokenIsValid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenIsValid()
    {
        ValidateToken.Response validResponse = new(IsValid: true, IsAuthFailure: false, MissingScopes: [], AccountName: null);
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubHandler(Result<ValidateToken.Response>.Ok(validResponse)));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsOkWithValidResult()
    {
        // Arrange
        object body = new { token = "ghp_valid", baseUrl = "https://api.github.com" };

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
            () => dto.IsValid.ShouldBeTrue(),
            () => dto.IsAuthFailure.ShouldBeFalse(),
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
