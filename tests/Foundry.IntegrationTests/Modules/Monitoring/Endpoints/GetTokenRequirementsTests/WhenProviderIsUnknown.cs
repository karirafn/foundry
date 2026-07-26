using System.Net;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetTokenRequirementsTests;

public sealed class WhenProviderIsUnknown : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenProviderIsUnknown()
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
        // Arrange — "bitbucket" is not in the catalog

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/providers/bitbucket/token-requirements", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
