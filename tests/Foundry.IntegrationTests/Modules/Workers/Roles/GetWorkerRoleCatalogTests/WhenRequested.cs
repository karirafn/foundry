using System.Net;
using System.Text.Json;

using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Roles.GetWorkerRoleCatalogTests;

public sealed class WhenRequested : IAsyncDisposable
{
    private static readonly Uri Endpoint = new("/api/worker-roles", UriKind.Relative);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRequested()
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
    public async Task BeforeTheImageBuildLoadsTheCatalog_ReturnsServiceUnavailable()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync(Endpoint, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("Worker.RoleCatalogNotLoaded");
    }

    [Fact]
    public async Task OnceTheCatalogIsLoaded_ReturnsTheRoleCardsAndPromptSections()
    {
        // Arrange — the image build publishes the catalog; publish one directly.
        WorkerRoleCatalogContent content = WorkerRoleCatalogReader.Parse(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["implement/.claude-plugin/plugin.json"] = """{"name":"implement","description":"Implements an issue."}""",
            ["implement/prompts/system.md"] = "You are implementing issue #{issueNumber}.\n\n{issueContent}",
            ["implement/prompts/task.md"] = "Implement issue #{issueNumber}.",
            ["implement/skills/tdd/SKILL.md"] = "---\nname: tdd\ndescription: Test first.\n---\nBody.",
            ["shared/.claude-plugin/plugin.json"] = """{"name":"shared"}""",
            ["shared/prompts/principles.md"] = "Engineering principles.",
        }).ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;

        _factory.Services.GetRequiredService<IWorkerRoleCatalogStore>().Publish(new WorkerRoleCatalog(
            content,
            new WorkerImageProvenance(
                "foundry-worker:local",
                "sha256:abc",
                new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero),
                "71c3f21",
                Verified: true)));

        // Act
        HttpResponseMessage response = await _client.GetAsync(Endpoint, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement root = document.RootElement;
        JsonElement role = root.GetProperty("roles")[0];
        JsonElement firstSection = root.GetProperty("promptSections")[0];
        root.ShouldSatisfyAllConditions(
            () => root.GetProperty("image").GetProperty("commit").GetString().ShouldBe("71c3f21"),
            () => root.GetProperty("image").GetProperty("verified").GetBoolean().ShouldBeTrue(),
            () => role.GetProperty("name").GetString().ShouldBe("implement"),
            () => role.GetProperty("purpose").GetString().ShouldBe("Implements an issue."),
            () => role.GetProperty("systemPrompt").GetString().ShouldNotBeNull().ShouldContain("{issueContent}"),
            () => role.GetProperty("skills")[0].GetProperty("description").GetString().ShouldBe("Test first."),
            () => role.GetProperty("verdictSchema").ValueKind.ShouldBe(JsonValueKind.Null),
            () => firstSection.GetProperty("key").GetString().ShouldBe("safety-preamble"),
            () => firstSection.GetProperty("condition").GetString().ShouldBe("Always"),
            () => root.GetProperty("rolePromptSlot").GetString().ShouldBe("{rolePrompt}"));
    }
}
