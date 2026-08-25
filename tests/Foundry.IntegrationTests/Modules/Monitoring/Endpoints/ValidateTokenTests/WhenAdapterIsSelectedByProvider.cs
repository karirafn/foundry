using System.Net;
using System.Net.Http.Json;
using System.Text;

using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.ValidateTokenTests;

public sealed class WhenAdapterIsSelectedByProvider
{
    [Fact]
    public async Task WhenProviderTypeIsGitHub_GitHubAdapterIsExercised()
    {
        // Arrange
        using RecordingHandler gitHubFake = RecordingHandler.ForGitHub();
        using RecordingHandler gitLabFake = RecordingHandler.ForGitLab();

        await using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.AddHttpClient<GitHubHttpClient>()
                .ConfigurePrimaryHttpMessageHandler(() => gitHubFake);
            services.AddHttpClient<GitLabHttpClient>()
                .ConfigurePrimaryHttpMessageHandler(() => gitLabFake);
        });
        using HttpClient client = factory.CreateClient();

        object body = new { token = "ghp_test", baseUrl = "https://github.com", providerType = "GitHub" };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        gitHubFake.WasCalled.ShouldBeTrue();
        gitLabFake.WasCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenProviderTypeIsGitLab_GitLabAdapterIsExercised()
    {
        // Arrange
        using RecordingHandler gitHubFake = RecordingHandler.ForGitHub();
        using RecordingHandler gitLabFake = RecordingHandler.ForGitLab();

        await using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.AddHttpClient<GitHubHttpClient>()
                .ConfigurePrimaryHttpMessageHandler(() => gitHubFake);
            services.AddHttpClient<GitLabHttpClient>()
                .ConfigurePrimaryHttpMessageHandler(() => gitLabFake);
        });
        using HttpClient client = factory.CreateClient();

        object body = new { token = "glpat_test", baseUrl = "https://gitlab.com", providerType = "GitLab" };

        // Act
        HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        gitLabFake.WasCalled.ShouldBeTrue();
        gitHubFake.WasCalled.ShouldBeFalse();
    }

    // Records whether it was called and returns a minimal valid provider-shaped response.
    // Handles multiple calls per request (GitLab validates in two HTTP calls).
    private sealed class RecordingHandler(string responseBody, string? scopesHeaderValue = null)
        : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }

        // Returns a minimal valid GitHub /user response with X-OAuth-Scopes header.
        public static RecordingHandler ForGitHub() =>
            new("""{"login":"testuser"}""", scopesHeaderValue: "repo");

        // Returns a minimal valid GitLab response for both /user and /personal_access_tokens/self.
        public static RecordingHandler ForGitLab() =>
            new("""{"username":"testuser","scopes":["api","read_api","read_repository","write_repository"]}""");

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };

            if (scopesHeaderValue is not null)
            {
                response.Headers.Add("X-OAuth-Scopes", scopesHeaderValue);
            }

            return Task.FromResult(response);
        }
    }
}
