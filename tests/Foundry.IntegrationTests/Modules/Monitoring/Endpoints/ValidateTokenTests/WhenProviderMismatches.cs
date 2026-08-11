using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.ValidateTokenTests;

public sealed class WhenProviderMismatches : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenProviderMismatches()
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
    public async Task WhenGitHubTokenSentToGitLab_ReturnsOkWithProviderMismatchKindNamingGitHub()
    {
        // Arrange — stub the handler to return a ProviderMismatch outcome naming GitHub as detected
        ValidateToken.Response mismatchResponse = new(
            Kind: ValidateToken.Kinds.ProviderMismatch,
            AccountName: null,
            MissingScopes: [],
            DetectedProvider: "github");

        await using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubHandler(Result<ValidateToken.Response>.Ok(mismatchResponse)));
        });
        using HttpClient client = factory.CreateClient();

        object body = new { token = "ghp_github_token", baseUrl = "https://gitlab.com", providerType = "gitlab" };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ValidateToken.Response? dto = await response.Content
            .ReadFromJsonAsync<ValidateToken.Response>(TestContext.Current.CancellationToken);
        dto.ShouldNotBeNull();
        dto.ShouldSatisfyAllConditions(
            () => dto.Kind.ShouldBe(ValidateToken.Kinds.ProviderMismatch),
            () => dto.DetectedProvider.ShouldBe("github"),
            () => dto.AccountName.ShouldBeNull(),
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
