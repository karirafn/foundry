using System.Net;
using System.Net.Http.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateWorkerLimitsTests;

public sealed class WhenValuesAreInvalid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenValuesAreInvalid()
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
    public async Task WhenMaxConcurrentIsZero_ReturnsBadRequest()
    {
        // Arrange
        object body = new { maxConcurrent = 0, timeoutMinutes = 60 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/limits", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenMaxConcurrentExceedsMaximum_ReturnsBadRequest()
    {
        // Arrange
        object body = new { maxConcurrent = 21, timeoutMinutes = 60 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/limits", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenTimeoutMinutesIsZero_ReturnsBadRequest()
    {
        // Arrange
        object body = new { maxConcurrent = 5, timeoutMinutes = 0 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/limits", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenTimeoutMinutesExceedsMaximum_ReturnsBadRequest()
    {
        // Arrange
        object body = new { maxConcurrent = 5, timeoutMinutes = 1441 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/limits", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
