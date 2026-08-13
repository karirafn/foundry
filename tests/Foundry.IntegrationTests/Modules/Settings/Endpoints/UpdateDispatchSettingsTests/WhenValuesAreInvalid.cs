using System.Net;
using System.Net.Http.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateDispatchSettingsTests;

public sealed class WhenValuesAreInvalid(FoundryWebAppFactory factory) : IClassFixture<FoundryWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task WhenProbeIntervalIsBelowMin_ReturnsBadRequest()
    {
        // Arrange
        object body = new { autoResumeOnUsageReset = true, probeIntervalMinutes = 4 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/dispatch", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenProbeIntervalIsZero_ReturnsBadRequest()
    {
        // Arrange
        object body = new { autoResumeOnUsageReset = true, probeIntervalMinutes = 0 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/dispatch", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
