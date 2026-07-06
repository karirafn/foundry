using System.Net;
using System.Net.Http.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.UpdateAuthModeTests;

public sealed class WhenPayloadIsInvalid(FoundryWebAppFactory factory) : IClassFixture<FoundryWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task WhenModeIsUnknown_ReturnsBadRequest()
    {
        // Arrange
        object body = new { mode = "unknown_mode" };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/credentials/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenApiKeyModeAndKeyIsMissing_ReturnsBadRequest()
    {
        // Arrange
        object body = new { mode = "api_key" };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/credentials/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
