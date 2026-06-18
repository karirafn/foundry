using System.Net;
using System.Net.Http.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdatePromptTemplatesTests;

public sealed class WhenPayloadIsInvalid(FoundryWebAppFactory factory) : IClassFixture<FoundryWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task WhenSystemPromptTemplateIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        object body = new
        {
            systemPromptTemplate = string.Empty,
            workerPromptTemplate = "Valid worker prompt.",
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/prompts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenWorkerPromptTemplateIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        object body = new
        {
            systemPromptTemplate = "Valid system prompt.",
            workerPromptTemplate = string.Empty,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/prompts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
