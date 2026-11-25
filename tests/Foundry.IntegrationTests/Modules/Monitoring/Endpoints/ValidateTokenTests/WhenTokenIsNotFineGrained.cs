using System.Net;
using System.Net.Http.Json;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.ValidateTokenTests;

public sealed class WhenTokenIsNotFineGrained : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenIsNotFineGrained()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
            services.AddHttpClient<GitHubHttpClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new GitHubUserHandler()));
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("ghp_")]
    [InlineData("gho_")]
    [InlineData("")]
    public async Task ValidateToken_ReturnsWrongTokenKindGitHubClassic(string prefix)
    {
        // Arrange
        object body = new { token = prefix + "classic", baseUrl = "https://github.com", providerType = "github" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ValidateToken.Response dto = (await response.Content
            .ReadFromJsonAsync<ValidateToken.Response>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        dto.ShouldSatisfyAllConditions(
            () => dto.Kind.ShouldBe("wrongTokenKind"),
            () => dto.WrongKind.ShouldBe("github-classic"));
    }

    [Fact]
    public async Task CreateAccount_IsRefusedAndNothingIsPersisted()
    {
        // Arrange
        object body = new { providerType = "github", baseUrl = "https://github.com", token = "ghp_" + "classic" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        List<CredentialSummary> accounts = (await _client.GetFromJsonAsync<List<CredentialSummary>>(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        response.ShouldSatisfyAllConditions(
            () => response.StatusCode.ShouldBe(HttpStatusCode.BadRequest),
            () => problem.Type.ShouldNotBeNull().ShouldEndWith("Credential.WrongTokenKind"),
            () => accounts.ShouldBeEmpty());
    }

    // Answers every request as GitHub's /user does for a token that authenticates.
    private sealed class GitHubUserHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"login":"octocat"}""", Encoding.UTF8, "application/json"),
            });
    }
}
