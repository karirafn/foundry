using System.Net;
using System.Net.Http.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateAuthModeTests;

public sealed class WhenPayloadIsInvalid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenPayloadIsInvalid()
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
    public async Task WhenModeIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        object body = new { mode = string.Empty, apiKey = "sk-ant-test" };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenModeIsInvalid_ReturnsBadRequest()
    {
        // Arrange
        object body = new { mode = "not_a_valid_mode", apiKey = "sk-ant-test" };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenApiKeyModeAndApiKeyIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        object body = new { mode = "api_key", apiKey = string.Empty };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenApiKeyModeAndApiKeyIsWhitespace_ReturnsBadRequest()
    {
        // Arrange
        object body = new { mode = "api_key", apiKey = "   " };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenApiKeyModeAndApiKeyIsMissing_ReturnsBadRequest()
    {
        // Arrange — no apiKey field
        object body = new { mode = "api_key" };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
