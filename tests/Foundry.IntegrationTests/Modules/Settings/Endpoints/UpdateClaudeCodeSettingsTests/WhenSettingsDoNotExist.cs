using System.Net;
using System.Net.Http.Json;

using Foundry.Testing;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateClaudeCodeSettingsTests;

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
    public async Task ReturnsNotFoundAsProblemDetails()
    {
        // Arrange — no settings seeded
        object body = new UpdateClaudeCodeSettingsRequestBuilder().Build();

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/claude-code", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Type.ShouldEndWith("Settings.NotFound");
    }
}
