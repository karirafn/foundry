using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Modules.Workers.Features.Roles.Models;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Roles.UpdateWorkerRoleModelTests;

public sealed class WhenSaved : IAsyncDisposable
{
    private const string ListedModel = "claude-opus-4-5-20251101";

    private static readonly Uri ModelsEndpoint = new("/api/worker-roles/models?refresh=true", UriKind.Relative);

    private readonly StubModelsApi _modelsApi = new();
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenSaved()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IClaudeModelsApi>();
            services.AddSingleton<IClaudeModelsApi>(_modelsApi);
            services.RemoveAll<IClaudeApiCredentialReader>();
            services.AddScoped<IClaudeApiCredentialReader, StubCredentialReader>();
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private Task<HttpResponseMessage> PutModelAsync(string role, string model) =>
        _client.PutAsJsonAsync(
            new Uri($"/api/worker-roles/{role}/model", UriKind.Relative),
            new { model },
            TestContext.Current.CancellationToken);

    private static async Task<string> ProblemTypeAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("type").GetString()!;
    }

    [Fact]
    public async Task ModelsEndpoint_ReturnsTheAliasesAndTheListedModels()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(ModelsEndpoint, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WorkerRoleModelOptionsResponse options = (await response.Content.ReadFromJsonAsync<WorkerRoleModelOptionsResponse>(
            FoundryWebAppFactory.JsonOptions,
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        options.ShouldSatisfyAllConditions(
            () => options.Aliases.ShouldBe(["fable", "opus", "sonnet", "haiku", "opus[1m]", "sonnet[1m]"]),
            () => options.Models.ShouldBe(
            [
                new WorkerRoleModelOption(ListedModel, "Claude Opus 4.5", false),
                new WorkerRoleModelOption("claude-3-7-sonnet-20250219", "Claude Sonnet 3.7", true),
            ]),
            () => options.RefreshError.ShouldBeNull());
    }

    [Fact]
    public async Task ModelsEndpoint_WhenTheApiIsUnreachable_StillReturnsTheAliasesAndTheError()
    {
        // Arrange
        _modelsApi.Fail = true;

        // Act
        HttpResponseMessage response = await _client.GetAsync(ModelsEndpoint, TestContext.Current.CancellationToken);

        // Assert
        WorkerRoleModelOptionsResponse options = (await response.Content.ReadFromJsonAsync<WorkerRoleModelOptionsResponse>(
            FoundryWebAppFactory.JsonOptions,
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        options.ShouldSatisfyAllConditions(
            () => options.Aliases.ShouldContain("opus"),
            () => options.Models.ShouldBeEmpty(),
            () => options.RefreshError.ShouldBe("The Models API could not be reached."));
    }

    [Theory]
    [InlineData("opus[1m]")]
    [InlineData(ListedModel)]
    [InlineData("")]
    public async Task WhenModelIsEmptyAnAliasOrListed_ReturnsOkWithTheSavedModel(string model)
    {
        // Act
        HttpResponseMessage response = await PutModelAsync("implement", model);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WorkerRoleModelResponse saved = (await response.Content.ReadFromJsonAsync<WorkerRoleModelResponse>(
            FoundryWebAppFactory.JsonOptions,
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        saved.ShouldBe(new WorkerRoleModelResponse("implement", model));
    }

    [Fact]
    public async Task WhenModelIsNotListed_ReturnsAFieldKeyedBadRequest()
    {
        // Act
        HttpResponseMessage response = await PutModelAsync("implement", "claude-typo");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTypeAsync(response)).ShouldEndWith("/Worker.RoleModelNotListed");
    }

    [Fact]
    public async Task WhenRoleIsUnknown_ReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await PutModelAsync("review", "opus");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemTypeAsync(response)).ShouldEndWith("/Worker.RoleNotFound");
    }

    [Fact]
    public async Task TheRoleCardCarriesTheSavedModel()
    {
        // Arrange
        WorkerRoleCatalogContent content = WorkerRoleCatalogReader.Parse(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["implement/.claude-plugin/plugin.json"] = """{"name":"implement","description":"Implements an issue."}""",
            ["implement/prompts/system.md"] = "You are implementing issue #{issueNumber}.\n\n{issueContent}",
            ["implement/prompts/task.md"] = "Implement issue #{issueNumber}.",
            ["triage/.claude-plugin/plugin.json"] = """{"name":"triage","description":"Triages an issue."}""",
            ["triage/prompts/system.md"] = "You are triaging issue #{issueNumber}.\n\n{issueContent}",
            ["triage/prompts/task.md"] = "Triage issue #{issueNumber}.",
            ["shared/.claude-plugin/plugin.json"] = """{"name":"shared"}""",
            ["shared/prompts/principles.md"] = "Engineering principles.",
            ["shared/prompts/principles/testing.md"] = "Testing:",
            ["shared/prompts/principles/design.md"] = "Design:",
            ["shared/prompts/principles/code.md"] = "Code:",
            ["shared/prompts/principles/domain.md"] = "Domain modelling:",
        }).ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
        _factory.Services.GetRequiredService<IWorkerRoleCatalogStore>().Publish(new WorkerRoleCatalog(
            content,
            new WorkerImageProvenance("foundry-worker:local", null, DateTimeOffset.UnixEpoch, null, Verified: true)));
        (await PutModelAsync("implement", "sonnet")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/worker-roles", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("roles")[1].GetProperty("model").GetString().ShouldBe("sonnet");
    }

    private sealed class StubCredentialReader : IClaudeApiCredentialReader
    {
        public Task<Result<ClaudeApiCredential>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<ClaudeApiCredential>.Ok(new ClaudeApiCredential.OAuthToken("sk-ant-oat01-test")));
    }

    private sealed class StubModelsApi : IClaudeModelsApi
    {
        public bool Fail { get; set; }

        public Task<Result<IReadOnlyList<ClaudeModel>>> ListAsync(ClaudeApiCredential credential, CancellationToken cancellationToken) =>
            Task.FromResult(Fail
                ? Result<IReadOnlyList<ClaudeModel>>.Fail(new Error(AnthropicModelsClient.FailedCode, "The Models API could not be reached."))
                : Result<IReadOnlyList<ClaudeModel>>.Ok(
                [
                    new ClaudeModel(ListedModel, "Claude Opus 4.5", "active"),
                    new ClaudeModel("claude-3-7-sonnet-20250219", "Claude Sonnet 3.7", "deprecated"),
                ]));
    }
}
