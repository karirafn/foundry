using System.Net;
using System.Text.Json;

using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.ImageBuild.GetWorkerImageToolchainTests;

public sealed class WhenRequested : IAsyncDisposable
{
    private const string ImageId = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly Uri Endpoint = new("/api/workers/image-toolchain", UriKind.Relative);

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
    public async Task BeforeAnyImageIsKnown_ReturnsUnknownWithoutAnImageId()
    {
        // Act
        JsonElement root = await GetAsync();

        // Assert
        root.ShouldSatisfyAllConditions(
            () => root.GetProperty("status").GetString().ShouldBe("unknown"),
            () => root.GetProperty("imageId").ValueKind.ShouldBe(JsonValueKind.Null));
    }

    [Fact]
    public async Task WhenTheCurrentImageHasNoStoredList_ReturnsUnknownWithItsImageId()
    {
        // Arrange
        PublishCatalog(ImageId);

        // Act
        JsonElement root = await GetAsync();

        // Assert
        root.ShouldSatisfyAllConditions(
            () => root.GetProperty("status").GetString().ShouldBe("unknown"),
            () => root.GetProperty("imageId").GetString().ShouldBe(ImageId));
    }

    [Fact]
    public async Task WhenTheCurrentImageHasAStoredList_ReturnsItsToolsInStoredOrder()
    {
        // Arrange
        PublishCatalog(ImageId);
        DateTimeOffset recordedAt = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);
        Toolchain toolchain = Toolchain.TryCreate([
            ToolchainEntry.TryCreate("node", "22.22.3")!,
            ToolchainEntry.TryCreate("docker", "29.9.3")!,
            ToolchainEntry.TryCreate("csharp-ls", "0.28.0")!,
        ])!;
        await StoreAsync(WorkerImageToolchain.Create(ImageId, toolchain, recordedAt));
        await StoreAsync(WorkerImageToolchain.Create(
            "sha256:older",
            Toolchain.TryCreate([ToolchainEntry.TryCreate("git", "2.47.0")!])!,
            recordedAt.AddDays(-1)));

        // Act
        JsonElement root = await GetAsync();

        // Assert
        JsonElement tools = root.GetProperty("tools");
        root.ShouldSatisfyAllConditions(
            () => root.GetProperty("status").GetString().ShouldBe("recorded"),
            () => root.GetProperty("imageId").GetString().ShouldBe(ImageId),
            () => root.GetProperty("recordedAt").GetDateTimeOffset().ShouldBe(recordedAt),
            () => tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ShouldBe(["node", "docker", "csharp-ls"]),
            () => tools.EnumerateArray().Select(tool => tool.GetProperty("version").GetString()).ShouldBe(["22.22.3", "29.9.3", "0.28.0"]));
    }

    private async Task<JsonElement> GetAsync()
    {
        HttpResponseMessage response = await _client.GetAsync(Endpoint, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }

    private void PublishCatalog(string imageId)
    {
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
            new WorkerImageProvenance(
                "foundry-worker:local",
                imageId,
                new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero),
                "71c3f21",
                Verified: true)));
    }

    private async Task StoreAsync(WorkerImageToolchain row)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Set<WorkerImageToolchain>().Add(row);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
