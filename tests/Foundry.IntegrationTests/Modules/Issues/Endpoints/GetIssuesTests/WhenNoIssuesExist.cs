using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenNoIssuesExist : IAsyncLifetime
{
    private readonly FoundryWebAppFactory _factory;
    private HttpClient _client = null!;

    public WhenNoIssuesExist()
    {
        _factory = new FoundryWebAppFactory();
    }

    public async ValueTask InitializeAsync()
    {
        await _factory.EnsureDatabaseCreatedAsync();
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsOkWithEmptyList()
    {
        // Arrange & Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<IssueSummary> summaries = (await response.Content.ReadFromJsonAsync<IReadOnlyList<IssueSummary>>(
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        summaries.ShouldBeEmpty();
    }
}
