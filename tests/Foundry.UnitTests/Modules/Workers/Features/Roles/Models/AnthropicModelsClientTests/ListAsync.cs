using System.Net;
using System.Text;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Workers.Features.Roles.Models;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Roles.Models.AnthropicModelsClientTests;

public sealed class ListAsync
{
    // Shape pinned from the 2026-11-06 spike against GET /v1/models with the workers' OAuth token.
    private const string FirstPage = """
        {
          "data": [
            {"type":"model","id":"claude-opus-4-5-20251101","display_name":"Claude Opus 4.5","created_at":"2025-11-01T00:00:00Z","lifecycle":"active"},
            {"type":"model","id":"claude-3-7-sonnet-20250219","display_name":"Claude Sonnet 3.7","created_at":"2025-02-19T00:00:00Z","lifecycle":"deprecated"}
          ],
          "has_more": true,
          "first_id": "claude-opus-4-5-20251101",
          "last_id": "claude-3-7-sonnet-20250219"
        }
        """;

    private const string SecondPage = """
        {
          "data": [
            {"type":"model","id":"claude-haiku-4-5-20251001","display_name":"Claude Haiku 4.5","created_at":"2025-10-01T00:00:00Z","lifecycle":{"status":"active"}}
          ],
          "has_more": false,
          "first_id": "claude-haiku-4-5-20251001",
          "last_id": "claude-haiku-4-5-20251001"
        }
        """;

    private static AnthropicModelsClient CreateSut(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = AnthropicModelsClient.BaseAddress });

    [Fact]
    public async Task WhenHasMore_FollowsAfterIdUntilTheEnd()
    {
        // Arrange
        RecordingHandler handler = new(FirstPage, SecondPage);
        AnthropicModelsClient sut = CreateSut(handler);

        // Act
        Result<IReadOnlyList<ClaudeModel>> result = await sut.ListAsync(
            new ClaudeApiCredential.OAuthToken("sk-ant-oat01-x"),
            TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<ClaudeModel> models = result.ShouldBeOfType<Result<IReadOnlyList<ClaudeModel>>.Success>().Value;
        models.Select(model => model.Id).ShouldBe(
            ["claude-opus-4-5-20251101", "claude-3-7-sonnet-20250219", "claude-haiku-4-5-20251001"]);
        handler.Requests.Select(request => request.RequestUri!.PathAndQuery).ShouldBe(
        [
            "/v1/models?limit=100",
            "/v1/models?limit=100&after_id=claude-3-7-sonnet-20250219",
        ]);
    }

    [Fact]
    public async Task ReadsDisplayNameAndMarksDeprecatedModels()
    {
        // Arrange
        AnthropicModelsClient sut = CreateSut(new RecordingHandler(FirstPage, SecondPage));

        // Act
        Result<IReadOnlyList<ClaudeModel>> result = await sut.ListAsync(
            new ClaudeApiCredential.OAuthToken("sk-ant-oat01-x"),
            TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<ClaudeModel> models = result.ShouldBeOfType<Result<IReadOnlyList<ClaudeModel>>.Success>().Value;
        models.ShouldSatisfyAllConditions(
            () => models[0].DisplayName.ShouldBe("Claude Opus 4.5"),
            () => models[0].Deprecated.ShouldBeFalse(),
            () => models[1].Deprecated.ShouldBeTrue(),
            () => models[2].Lifecycle.ShouldBe("active"));
    }

    [Fact]
    public async Task WhenOAuthToken_SendsBearerAuthorizationAndTheApiVersion()
    {
        // Arrange
        RecordingHandler handler = new(SecondPage);
        AnthropicModelsClient sut = CreateSut(handler);

        // Act
        await sut.ListAsync(new ClaudeApiCredential.OAuthToken("sk-ant-oat01-x"), TestContext.Current.CancellationToken);

        // Assert
        HttpRequestMessage request = handler.Requests.ShouldHaveSingleItem();
        request.ShouldSatisfyAllConditions(
            () => request.Headers.Authorization!.Scheme.ShouldBe("Bearer"),
            () => request.Headers.Authorization!.Parameter.ShouldBe("sk-ant-oat01-x"),
            () => request.Headers.GetValues("anthropic-version").ShouldBe([AnthropicModelsClient.AnthropicVersion]),
            () => request.Headers.Contains("anthropic-beta").ShouldBeFalse(),
            () => request.Headers.Contains("x-api-key").ShouldBeFalse());
    }

    [Fact]
    public async Task WhenApiKey_SendsXApiKey()
    {
        // Arrange
        RecordingHandler handler = new(SecondPage);
        AnthropicModelsClient sut = CreateSut(handler);

        // Act
        await sut.ListAsync(new ClaudeApiCredential.ApiKey("sk-ant-api03-x"), TestContext.Current.CancellationToken);

        // Assert
        HttpRequestMessage request = handler.Requests.ShouldHaveSingleItem();
        request.ShouldSatisfyAllConditions(
            () => request.Headers.GetValues("x-api-key").ShouldBe(["sk-ant-api03-x"]),
            () => request.Headers.Authorization.ShouldBeNull());
    }

    [Fact]
    public async Task WhenTheApiRejectsTheCredential_FailsWithTheStatus()
    {
        // Arrange
        RecordingHandler handler = new(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"type":"error","error":{"type":"authentication_error"}}"""),
        });
        AnthropicModelsClient sut = CreateSut(handler);

        // Act
        Result<IReadOnlyList<ClaudeModel>> result = await sut.ListAsync(
            new ClaudeApiCredential.OAuthToken("sk-ant-oat01-secret"),
            TestContext.Current.CancellationToken);

        // Assert
        Error error = result.ShouldBeOfType<Result<IReadOnlyList<ClaudeModel>>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(AnthropicModelsClient.FailedCode),
            () => error.Message.ShouldContain("401"),
            () => error.Message.ShouldNotContain("sk-ant-oat01-secret"));
    }

    [Fact]
    public async Task WhenTheApiIsUnreachable_Fails()
    {
        // Arrange
        RecordingHandler handler = new(new HttpRequestException("Name or service not known"));
        AnthropicModelsClient sut = CreateSut(handler);

        // Act
        Result<IReadOnlyList<ClaudeModel>> result = await sut.ListAsync(
            new ClaudeApiCredential.OAuthToken("sk-ant-oat01-x"),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IReadOnlyList<ClaudeModel>>.Failure>().Error.Code
            .ShouldBe(AnthropicModelsClient.FailedCode);
    }

    [Fact]
    public async Task WhenTheResponseHasNoDataArray_Fails()
    {
        // Arrange
        AnthropicModelsClient sut = CreateSut(new RecordingHandler("""{"models":[]}"""));

        // Act
        Result<IReadOnlyList<ClaudeModel>> result = await sut.ListAsync(
            new ClaudeApiCredential.OAuthToken("sk-ant-oat01-x"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheServerKeepsAnsweringTheSameCursor_StopsInsteadOfLooping()
    {
        // Arrange — has_more stays true but last_id never advances
        RecordingHandler handler = new(Enumerable.Repeat(FirstPage, AnthropicModelsClient.MaxPages + 5).ToArray());
        AnthropicModelsClient sut = CreateSut(handler);

        // Act
        Result<IReadOnlyList<ClaudeModel>> result = await sut.ListAsync(
            new ClaudeApiCredential.OAuthToken("sk-ant-oat01-x"),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<IReadOnlyList<ClaudeModel>>.Success>().Value.Count.ShouldBe(2);
        handler.Requests.Count.ShouldBe(2);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public RecordingHandler(params string[] bodies)
        {
            foreach (string body in bodies)
            {
                _responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }
        }

        public RecordingHandler(HttpResponseMessage response)
        {
            _responses.Enqueue(() => response);
        }

        public RecordingHandler(Exception exception)
        {
            _responses.Enqueue(() => throw exception);
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue()());
        }
    }
}
