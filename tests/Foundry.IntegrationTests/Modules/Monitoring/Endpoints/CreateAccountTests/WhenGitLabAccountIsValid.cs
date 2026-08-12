using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

public sealed class WhenGitLabAccountIsValid : IAsyncDisposable
{
    private const string ResolvedAccountName = "gitlab-user";

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenGitLabAccountIsValid()
    {
        ValidateToken.Response validResponse = new(
            Kind: ValidateToken.Kinds.Authenticated,
            AccountName: ResolvedAccountName,
            MissingScopes: [],
            DetectedProvider: null);

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
    public async Task ReturnsCreatedWithCredentialSummary()
    {
        // Arrange
        object body = new
        {
            providerType = "gitlab",
            baseUrl = "https://gitlab.com",
            token = "glpat_test_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        CredentialCreationResult? result = await response.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        CredentialSummary account = result.Credential;
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe(ResolvedAccountName),
            () => account.ProviderType.ShouldBe("gitlab"),
            () => account.BaseUrl.ShouldBe("https://gitlab.com/"),
            () => account.HasToken.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenProviderTypeIsMixedCase_ReturnsCreated()
    {
        // Arrange
        object body = new
        {
            providerType = "GitLab",
            baseUrl = "https://gitlab.com",
            token = "glpat_test_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task WhenSelfHosted_ReturnsCreatedWithCorrectBaseUrl()
    {
        // Arrange
        object body = new
        {
            providerType = "gitlab",
            baseUrl = "https://gitlab.example.com",
            token = "glpat_test_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        CredentialCreationResult? result = await response.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Credential.BaseUrl.ShouldBe("https://gitlab.example.com/");
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
