using System.Net;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssueByIdTests;

public sealed class WhenIssueDoesNotExist : IAsyncLifetime
{
    private readonly FoundryWebAppFactory _factory;
    private HttpClient _client = null!;

    public WhenIssueDoesNotExist()
    {
        _factory = new FoundryWebAppFactory();
    }

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsNotFound()
    {
        // Arrange
        Guid nonExistentId = Guid.NewGuid();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/issues/{nonExistentId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
