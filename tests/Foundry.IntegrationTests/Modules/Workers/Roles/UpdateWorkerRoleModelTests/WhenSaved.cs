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
        HttpResponseMessage response = await PutModelAsync("triage", model);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WorkerRoleModelResponse saved = (await response.Content.ReadFromJsonAsync<WorkerRoleModelResponse>(
            FoundryWebAppFactory.JsonOptions,
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        saved.ShouldBe(new WorkerRoleModelResponse("triage", model));
    }

    [Fact]
    public async Task WhenModelIsNotListed_ReturnsAFieldKeyedBadRequest()
    {
        // Act
        HttpResponseMessage response = await PutModelAsync("triage", "claude-typo");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTypeAsync(response)).ShouldEndWith("/Worker.RoleModelNotListed");
    }

    [Fact]
    public async Task WhenRoleIsUnknown_ReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await PutModelAsync("retired-role", "opus");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemTypeAsync(response)).ShouldEndWith("/Worker.RoleNotFound");
    }

    private void PublishCatalog()
    {
        WorkerRoleCatalogContent content = WorkerRoleCatalogReader.Parse(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["implement/.claude-plugin/plugin.json"] = """{"name":"implement","description":"Implements an issue."}""",
            ["implement/prompts/system.md"] = "You are implementing issue #{issueNumber}.\n\n{issueContent}",
            ["implement/prompts/task.md"] = "Implement issue #{issueNumber}.",
            ["triage/.claude-plugin/plugin.json"] = """{"name":"triage","description":"Triages an issue."}""",
            ["triage/prompts/system.md"] = "You are triaging issue #{issueNumber}.\n\n{issueContent}",
            ["triage/prompts/task.md"] = "Triage issue #{issueNumber}.",
            ["review/.claude-plugin/plugin.json"] = """{"name":"review","description":"Reviews a pull request."}""",
            ["review/prompts/system.md"] = "You are reviewing issue #{issueNumber}.\n\n{issueContent}",
            ["review/prompts/task.md"] = "Review issue #{issueNumber}.",
            ["shared/.claude-plugin/plugin.json"] = """{"name":"shared"}""",
            ["shared/prompts/principles.md"] = "Engineering principles.",
            ["shared/prompts/decisions.md"] = "Ask only about uncertain business logic.",
            ["shared/prompts/principles/testing.md"] = "Testing:",
            ["shared/prompts/principles/design.md"] = "Design:",
            ["shared/prompts/principles/code.md"] = "Code:",
            ["shared/prompts/principles/domain.md"] = "Domain modelling:",
        }).ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
        _factory.Services.GetRequiredService<IWorkerRoleCatalogStore>().Publish(new WorkerRoleCatalog(
            content,
            new WorkerImageProvenance("foundry-worker:local", null, DateTimeOffset.UnixEpoch, null, Verified: true)));
    }

    private Task<HttpResponseMessage> PutDemandModelAsync(string demand, string model, string effort) =>
        _client.PutAsJsonAsync(
            new Uri($"/api/worker-roles/implement/demand-models/{demand}", UriKind.Relative),
            new { model, effort },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenRoleIsImplement_ReturnsABadRequestNamingThePerDemandSetting()
    {
        // Act
        HttpResponseMessage response = await PutModelAsync("implement", "opus");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTypeAsync(response)).ShouldEndWith("/Worker.RoleModelPerDemand");
    }

    [Fact]
    public async Task WhenADemandModelIsSaved_ReturnsOkWithTheSavedValues()
    {
        // Act
        HttpResponseMessage response = await PutDemandModelAsync("routine", $" {ListedModel} ", "high");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement saved = document.RootElement;
        saved.ShouldSatisfyAllConditions(
            () => saved.GetProperty("demand").GetString().ShouldBe("routine"),
            () => saved.GetProperty("model").GetString().ShouldBe(ListedModel),
            () => saved.GetProperty("effort").GetString().ShouldBe("high"));
    }

    [Fact]
    public async Task WhenADemandModelEffortIsInvalid_ReturnsABadRequest()
    {
        // Act
        HttpResponseMessage response = await PutDemandModelAsync("demanding", "opus", "max");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTypeAsync(response)).ShouldEndWith("/Worker.TierEffortInvalid");
    }

    [Fact]
    public async Task WhenADemandModelIsNotListed_ReturnsABadRequest()
    {
        // Act
        HttpResponseMessage response = await PutDemandModelAsync("demanding", "claude-typo", string.Empty);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTypeAsync(response)).ShouldEndWith("/Worker.RoleModelNotListed");
    }

    [Fact]
    public async Task WhenTheDemandIsUnknown_ReturnsNotFound()
    {
        // Act
        HttpResponseMessage response = await PutDemandModelAsync("urgent", "opus", string.Empty);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheImplementCardCarriesItsDemandModelsRoutineFirstAndNoRoleModel()
    {
        // Arrange
        PublishCatalog();
        (await PutDemandModelAsync("demanding", "opus", "xhigh")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PutDemandModelAsync("routine", "sonnet", string.Empty)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/worker-roles", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement implement = document.RootElement.GetProperty("roles")[1];
        string[] demandModels =
        [
            .. implement.GetProperty("demandModels").EnumerateArray().Select(entry =>
                $"{entry.GetProperty("demand").GetString()}:{entry.GetProperty("model").GetString()}:{entry.GetProperty("effort").GetString()}"),
        ];
        implement.ShouldSatisfyAllConditions(
            () => implement.GetProperty("name").GetString().ShouldBe("implement"),
            () => implement.GetProperty("model").GetString().ShouldBeEmpty(),
            () => demandModels.ShouldBe(["routine:sonnet:", "demanding:opus:xhigh"]));
    }

    [Fact]
    public async Task TheRoleCardCarriesTheSavedModel()
    {
        // Arrange
        PublishCatalog();
        (await PutModelAsync("triage", "sonnet")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/worker-roles", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement triage = document.RootElement.GetProperty("roles")[0];
        triage.ShouldSatisfyAllConditions(
            () => triage.GetProperty("model").GetString().ShouldBe("sonnet"),
            () => triage.GetProperty("demandModels").GetArrayLength().ShouldBe(0));
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
