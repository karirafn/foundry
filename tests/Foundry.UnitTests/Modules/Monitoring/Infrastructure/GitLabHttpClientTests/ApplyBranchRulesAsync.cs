using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class ApplyBranchRulesAsync
{
    private const string BranchUri = "https://gitlab.com/api/v4/projects/group%2Fproject/protected_branches/main";

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient CreateSut(HttpClient httpClient) =>
        new(
            httpClient,
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenBranchIsUnprotected_ProtectsItWithPost()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.NotFound, """{ "message": "404 Not found" }"""),
            (HttpStatusCode.Created, """{ "name": "main" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.ApplyBranchRulesAsync(
            ValidBaseUrl, ValidSlug, "main", "glpat_rules", CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Post),
            r => r.RequestUri!.AbsoluteUri.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/protected_branches"),
            r => r.Headers.GetValues("PRIVATE-TOKEN").ShouldBe(["glpat_rules"]));
        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        body.RootElement.ShouldSatisfyAllConditions(
            b => b.GetProperty("name").GetString().ShouldBe("main"),
            b => b.GetProperty("push_access_level").GetInt32().ShouldBe(0),
            b => b.GetProperty("merge_access_level").GetInt32().ShouldBe(40),
            b => b.GetProperty("allow_force_push").GetBoolean().ShouldBeFalse());
    }

    [Fact]
    public async Task WhenBranchIsProtected_PatchesItReplacingEveryPushGrantWithNoOne()
    {
        // Arrange
        string existingJson = """
            {
              "name": "main",
              "allow_force_push": true,
              "push_access_levels": [
                { "id": 11, "access_level": 40, "access_level_description": "Maintainers" },
                { "id": 12, "access_level": 40, "user_id": 5 }
              ],
              "merge_access_levels": [{ "id": 21, "access_level": 30 }]
            }
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, existingJson),
            (HttpStatusCode.OK, """{ "name": "main" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.ApplyBranchRulesAsync(
            ValidBaseUrl, ValidSlug, "main", "glpat_rules", CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Patch),
            r => r.RequestUri!.AbsoluteUri.ShouldBe(BranchUri));
        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        JsonElement root = body.RootElement;
        root.GetProperty("allow_force_push").GetBoolean().ShouldBeFalse();
        root.TryGetProperty("allowed_to_merge", out _).ShouldBeFalse();
        root.TryGetProperty("allowed_to_unprotect", out _).ShouldBeFalse();
        JsonElement[] allowedToPush = [.. root.GetProperty("allowed_to_push").EnumerateArray()];
        allowedToPush.Length.ShouldBe(3);
        allowedToPush[0].GetProperty("id").GetInt64().ShouldBe(11);
        allowedToPush[0].GetProperty("_destroy").GetBoolean().ShouldBeTrue();
        allowedToPush[1].GetProperty("id").GetInt64().ShouldBe(12);
        allowedToPush[1].GetProperty("_destroy").GetBoolean().ShouldBeTrue();
        allowedToPush[2].GetProperty("access_level").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task WhenBranchAlreadyHasNoOneGrant_KeepsItAndDestroysTheRest()
    {
        // Arrange
        string existingJson = """
            {
              "name": "main",
              "allow_force_push": false,
              "push_access_levels": [
                { "id": 11, "access_level": 0 },
                { "id": 12, "access_level": 30, "group_id": 9 }
              ]
            }
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, existingJson),
            (HttpStatusCode.OK, """{ "name": "main" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        await sut.ApplyBranchRulesAsync(ValidBaseUrl, ValidSlug, "main", "glpat_rules", CancellationToken.None);

        // Assert
        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        JsonElement[] allowedToPush = [.. body.RootElement.GetProperty("allowed_to_push").EnumerateArray()];
        allowedToPush.Length.ShouldBe(1);
        allowedToPush[0].GetProperty("id").GetInt64().ShouldBe(12);
        allowedToPush[0].GetProperty("_destroy").GetBoolean().ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NeverUnprotectsTheBranch(bool branchIsProtected)
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            branchIsProtected
                ? (HttpStatusCode.OK, """{ "name": "main", "allow_force_push": true, "push_access_levels": [] }""")
                : (HttpStatusCode.NotFound, "{}"),
            (HttpStatusCode.OK, "{}"),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        await sut.ApplyBranchRulesAsync(ValidBaseUrl, ValidSlug, "main", "glpat_rules", CancellationToken.None);

        // Assert
        handler.Requests.ShouldAllBe(r => r.Method != HttpMethod.Delete);
    }

    [Fact]
    public async Task WhenWriteReturnsForbidden_FailsWithInsufficientPermission()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.NotFound, "{}"),
            (HttpStatusCode.Forbidden, """{ "message": "403 Forbidden" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.ApplyBranchRulesAsync(
            ValidBaseUrl, ValidSlug, "main", "glpat_rules", CancellationToken.None);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(BranchRuleWriteErrors.InsufficientPermissionCode);
        failure.Error.Message.ShouldContain("Maintainer");
    }

    [Fact]
    public async Task WhenWriteFailsOtherwise_FailsWithProviderErrorCarryingProviderMessage()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.NotFound, "{}"),
            (HttpStatusCode.UnprocessableEntity, """{ "message": { "name": ["has already been taken"] } }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.ApplyBranchRulesAsync(
            ValidBaseUrl, ValidSlug, "main", "glpat_rules", CancellationToken.None);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(GitLabErrors.ProviderError(string.Empty).Code);
        failure.Error.Message.ShouldContain("has already been taken");
    }
}
