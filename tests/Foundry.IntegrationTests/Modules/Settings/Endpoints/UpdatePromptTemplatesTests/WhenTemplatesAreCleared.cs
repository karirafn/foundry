using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdatePromptTemplatesTests;

/// <summary>
/// The new contract requires all three prompt fields to be non-empty strings.
/// Sending empty or null values is rejected with 400.
/// </summary>
public sealed class WhenTemplatesAreCleared(FoundryWebAppFactory factory) : IClassFixture<FoundryWebAppFactory>
{
    private const int BadRequestStatus = 400;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task WhenSystemPromptTemplateIsNull_ReturnsBadRequest()
    {
        // Arrange
        object body = new
        {
            systemPromptTemplate = (string?)null,
            workerPromptTemplate = "Valid worker prompt with {issueNumber}.",
            branchNamingInstruction = "Use conventional branch naming",
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
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenWorkerPromptTemplateIsNull_ReturnsBadRequest()
    {
        // Arrange
        object body = new
        {
            systemPromptTemplate = "Valid system prompt.",
            workerPromptTemplate = (string?)null,
            branchNamingInstruction = "Use conventional branch naming",
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
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }
}
