using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

public sealed class WhenAccountNameIsDuplicate : IAsyncDisposable
{
    // Token-keyed routing: each token maps to a fixed account name.
    // ghp_first_token  → first-user  (create first account)
    // ghp_second_token → second-user (create second account)
    // ghp_colliding_token → first-user (update second account; triggers conflict)
    private const string FirstToken = "ghp_first_token";
    private const string SecondToken = "ghp_second_token";
    private const string CollidingToken = "ghp_colliding_token";
    private const string FirstAccountName = "first-user";
    private const string SecondAccountName = "second-user";

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly TokenKeyedStub _stub;

    public WhenAccountNameIsDuplicate()
    {
        _stub = new TokenKeyedStub(new Dictionary<string, string>
        {
            [FirstToken] = FirstAccountName,
            [SecondToken] = SecondAccountName,
            [CollidingToken] = FirstAccountName,
        });
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(_ => _stub);
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsConflict()
    {
        // Arrange — create two accounts with distinct names, then update the second
        // to use a token that resolves to the first account's name.
        object firstBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = FirstToken,
        };

        object secondBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = SecondToken,
        };

        await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            firstBody,
            TestContext.Current.CancellationToken);

        HttpResponseMessage secondResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            secondBody,
            TestContext.Current.CancellationToken);

        CredentialSummary? second = await secondResponse.Content
            .ReadFromJsonAsync<CredentialSummary>(TestContext.Current.CancellationToken);
        second.ShouldNotBeNull();

        // Update second account with a token that resolves to the first account's name
        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = CollidingToken,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{second.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task WhenNoTokenSupplied_NameUnchangedDoesNotConflict()
    {
        // Arrange — updating without a token keeps the existing name; no conflict expected
        ValidateToken.Response validResponse = new(
            IsValid: true,
            IsAuthFailure: false,
            MissingScopes: [],
            AccountName: "octocat");

        using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(validResponse)));
        });
        using HttpClient client = factory.CreateClient();

        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        CredentialSummary? created = await createResponse.Content
            .ReadFromJsonAsync<CredentialSummary>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();

        object updateBody = new { baseUrl = "https://github.com" };

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Routes each ValidateToken call to a fixed account name based on the token value.
    // This is robust against extra internal calls — the result depends solely on the token.
    private sealed class TokenKeyedStub(Dictionary<string, string> tokenToName)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken)
        {
            string accountName = tokenToName.TryGetValue(query.Token, out string? name)
                ? name
                : "default-user";
            ValidateToken.Response response = new(
                IsValid: true,
                IsAuthFailure: false,
                MissingScopes: [],
                AccountName: accountName);
            return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
        }
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
