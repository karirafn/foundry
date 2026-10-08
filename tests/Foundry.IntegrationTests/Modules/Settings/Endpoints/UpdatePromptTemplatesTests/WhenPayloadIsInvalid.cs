using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdatePromptTemplatesTests;

public sealed class WhenPayloadIsInvalid(FoundryWebAppFactory factory) : IClassFixture<FoundryWebAppFactory>
{
    private const int BadRequestStatus = 400;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task WhenSystemPromptTemplateIsEmpty_ReturnsBadRequestAsProblemDetails()
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
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.ShouldSatisfyAllConditions(
            () => problem.Status.ShouldBe(BadRequestStatus),
            () => problem.Type.ShouldEndWith("Settings.InvalidPromptTemplate"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenWorkerPromptTemplateIsEmpty_ReturnsBadRequestAsProblemDetails()
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
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.ShouldSatisfyAllConditions(
            () => problem.Status.ShouldBe(BadRequestStatus),
            () => problem.Type.ShouldEndWith("Settings.InvalidPromptTemplate"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }
}
