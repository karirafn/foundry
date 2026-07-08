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

public sealed class WhenRequestIsValid : IAsyncDisposable
{
    private const string InitialAccountName = "octocat";

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRequestIsValid()
    {
        ValidateToken.Response validResponse = new(
            IsValid: true,
            IsAuthFailure: false,
            MissingScopes: [],
            AccountName: InitialAccountName);
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
    public async Task WhenNoTokenSupplied_KeepsExistingName()
    {
        // Arrange
        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_original_token",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        AccountSummary? created = await createResponse.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();

        object updateBody = new
        {
            baseUrl = "https://github.com",
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AccountSummary? account = await response.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        account.ShouldNotBeNull();
        account.Name.ShouldBe(InitialAccountName);
    }

    [Fact]
    public async Task WhenTokenProvided_RenamesAccountToResolvedIdentity()
    {
        // Arrange — token-keyed routing: original token yields InitialAccountName, new token yields NewIdentity.
        const string OriginalToken = "ghp_original_token";
        const string NewToken = "ghp_new_token";
        const string NewIdentity = "new-identity";

        TokenKeyedStub tokenKeyedStub = new(new Dictionary<string, string>
        {
            [OriginalToken] = InitialAccountName,
            [NewToken] = NewIdentity,
        });

        using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddSingleton<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(tokenKeyedStub);
        });
        using HttpClient client = factory.CreateClient();

        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = OriginalToken,
        };

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        AccountSummary? created = await createResponse.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = NewToken,
        };

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AccountSummary? account = await response.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        account.ShouldNotBeNull();
        account.Name.ShouldBe(NewIdentity);
    }

    [Fact]
    public async Task WhenTokenProvided_UpdatesToken()
    {
        // Arrange
        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_original_token",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        AccountSummary? created = await createResponse.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = "ghp_new_token",
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AccountSummary? account = await response.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        account.ShouldNotBeNull();
        account.ShouldSatisfyAllConditions(
            () => account.HasToken.ShouldBeTrue(),
            () => account.Name.ShouldBe(InitialAccountName));
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
