using System.Net;
using System.Net.Http.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

public sealed class WhenRequestIsInvalid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRequestIsInvalid()
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
    public async Task WhenNameIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        object body = new { name = string.Empty, providerType = "github", baseUrl = "https://github.com", token = "ghp_test" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenNameIsWhitespace_ReturnsBadRequest()
    {
        // Arrange
        object body = new { name = "   ", providerType = "github", baseUrl = "https://github.com", token = "ghp_test" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenBaseUrlIsNotHttps_ReturnsBadRequest()
    {
        // Arrange
        object body = new { name = "My Account", providerType = "github", baseUrl = "http://github.com", token = "ghp_test" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenBaseUrlIsNotAbsolute_ReturnsBadRequest()
    {
        // Arrange
        object body = new { name = "My Account", providerType = "github", baseUrl = "not-a-url", token = "ghp_test" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenTokenIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        object body = new { name = "My Account", providerType = "github", baseUrl = "https://github.com", token = string.Empty };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenTokenIsWhitespace_ReturnsBadRequest()
    {
        // Arrange
        object body = new { name = "My Account", providerType = "github", baseUrl = "https://github.com", token = "   " };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenProviderTypeIsUnsupported_ReturnsBadRequest()
    {
        // Arrange
        object body = new { name = "My Account", providerType = "bitbucket", baseUrl = "https://bitbucket.org", token = "abc_test" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
