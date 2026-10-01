using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenNoIssuesExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenNoIssuesExist()
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
    public async Task ReturnsOkWithEmptyList()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedIssues? result = await response.Content.ReadFromJsonAsync<PagedIssues>(
            TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Items.ShouldBeEmpty();
        result.NextCursor.ShouldBeNull();
    }
}
