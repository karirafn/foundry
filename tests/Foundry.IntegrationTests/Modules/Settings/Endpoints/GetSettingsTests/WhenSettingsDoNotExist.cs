using System.Net;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.GetSettingsTests;

public sealed class WhenSettingsDoNotExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenSettingsDoNotExist()
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
    public async Task ReturnsNotFound()
    {
        // Arrange — no settings seeded; SettingsSeeder is a hosted service and is removed in tests

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
