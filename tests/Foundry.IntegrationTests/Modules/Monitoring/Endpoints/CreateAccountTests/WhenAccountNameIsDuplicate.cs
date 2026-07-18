using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

/// <summary>
/// Verifies that creating two credentials with the same resolved account name is now allowed —
/// the (base_url, name) unique index was dropped in favour of per-namespace scoping.
/// </summary>
public sealed class WhenAccountNameIsDuplicate : IAsyncDisposable
{
    private const string ResolvedAccountName = "octocat";

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountNameIsDuplicate()
    {
        ValidateToken.Response validResponse = new(
            IsValid: true,
            IsAuthFailure: false,
            MissingScopes: [],
            AccountName: ResolvedAccountName);

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(validResponse)));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsCreated()
    {
        // Arrange — same token stub always returns "octocat" as the account name.
        object body = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        HttpResponseMessage firstResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act — create a second credential with the same resolved name; now allowed.
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert — both credentials are accepted; duplicate names no longer conflict.
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private sealed class StubValidateTokenHandler(Result<ValidateToken.Response> result)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
