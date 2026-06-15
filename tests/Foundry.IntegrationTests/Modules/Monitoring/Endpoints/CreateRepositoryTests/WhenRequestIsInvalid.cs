using System.Net;
using System.Net.Http.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateRepositoryTests;

public sealed class WhenRequestIsInvalid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _accountId;

    public WhenRequestIsInvalid()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
        _accountId = Guid.NewGuid();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenSlugIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        object body = new { slug = string.Empty };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{_accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenSlugIsWhitespace_ReturnsBadRequest()
    {
        // Arrange
        object body = new { slug = "   " };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{_accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenPollIntervalIsZero_ReturnsBadRequest()
    {
        // Arrange
        object body = new { slug = "owner/repo", pollIntervalSeconds = 0 };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{_accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenPollIntervalIsNegative_ReturnsBadRequest()
    {
        // Arrange
        object body = new { slug = "owner/repo", pollIntervalSeconds = -1 };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{_accountId}/repositories", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
