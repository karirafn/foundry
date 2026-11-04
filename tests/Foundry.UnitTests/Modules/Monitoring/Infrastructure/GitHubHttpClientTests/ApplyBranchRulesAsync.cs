using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class ApplyBranchRulesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient CreateSut(HttpClient httpClient) =>
        new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenNoFoundryRulesetExists_CreatesRulesetWithPost()
    {
        // Arrange
        string listJson = """[{ "id": 7, "name": "someone-elses", "source_type": "Repository" }]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.Created, """{ "id": 42 }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.ApplyBranchRulesAsync(ValidBaseUrl, ValidSlug, "rules_token", CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Get),
            r => r.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/rulesets"),
            r => r.RequestUri!.Query.ShouldContain("includes_parents=false"),
            r => r.Headers.Authorization!.Parameter.ShouldBe("rules_token"));
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Post),
            r => r.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/rulesets"),
            r => r.Headers.Authorization!.Parameter.ShouldBe("rules_token"));
    }

    [Fact]
    public async Task WhenFoundryRulesetExists_UpdatesItWithPutInsteadOfDuplicating()
    {
        // Arrange
        string listJson = """
            [
              { "id": 7, "name": "other", "source_type": "Repository" },
              { "id": 42, "name": "foundry-eligibility", "source_type": "Repository", "enforcement": "disabled" }
            ]
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, listJson),
            (HttpStatusCode.OK, """{ "id": 42 }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.ApplyBranchRulesAsync(ValidBaseUrl, ValidSlug, "rules_token", CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Put),
            r => r.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/rulesets/42"));
        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        body.RootElement.GetProperty("enforcement").GetString().ShouldBe("active");
    }

    [Fact]
    public async Task WritesTheEligibilityRulesetShape()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.Created, """{ "id": 42 }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        await sut.ApplyBranchRulesAsync(ValidBaseUrl, ValidSlug, "rules_token", CancellationToken.None);

        // Assert
        using JsonDocument body = JsonDocument.Parse(handler.RequestBodies[1]!);
        JsonElement root = body.RootElement;
        JsonElement[] rules = [.. root.GetProperty("rules").EnumerateArray()];
        JsonElement pullRequest = rules.Single(r => r.GetProperty("type").GetString() == "pull_request");
        JsonElement parameters = pullRequest.GetProperty("parameters");
        root.ShouldSatisfyAllConditions(
            r => r.GetProperty("name").GetString().ShouldBe("foundry-eligibility"),
            r => r.GetProperty("target").GetString().ShouldBe("branch"),
            r => r.GetProperty("enforcement").GetString().ShouldBe("active"),
            r => r.GetProperty("conditions").GetProperty("ref_name").GetProperty("include")
                .EnumerateArray().Select(e => e.GetString()).ShouldBe(["~DEFAULT_BRANCH"]),
            r => r.GetProperty("bypass_actors").GetArrayLength().ShouldBe(0),
            _ => rules.Select(rule => rule.GetProperty("type").GetString())
                .ShouldBe(["pull_request", "non_fast_forward", "deletion"]),
            _ => parameters.GetProperty("required_approving_review_count").GetInt32().ShouldBe(0),
            _ => parameters.GetProperty("dismiss_stale_reviews_on_push").GetBoolean().ShouldBeFalse(),
            _ => parameters.GetProperty("require_code_owner_review").GetBoolean().ShouldBeFalse(),
            _ => parameters.GetProperty("require_last_push_approval").GetBoolean().ShouldBeFalse(),
            _ => parameters.GetProperty("required_review_thread_resolution").GetBoolean().ShouldBeFalse(),
            _ => rules.Where(rule => rule.GetProperty("type").GetString() != "pull_request")
                .ShouldAllBe(rule => !rule.TryGetProperty("parameters", out _)));
    }

    [Fact]
    public async Task WhenWriteReturnsForbidden_FailsWithInsufficientPermission()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.Forbidden, """{ "message": "Resource not accessible by personal access token" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.ApplyBranchRulesAsync(ValidBaseUrl, ValidSlug, "rules_token", CancellationToken.None);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(BranchRuleWriteErrors.InsufficientPermissionCode);
        failure.Error.Message.ShouldContain("Administration");
    }

    [Fact]
    public async Task WhenListReturnsForbidden_FailsWithInsufficientPermissionWithoutWriting()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.Forbidden, """{ "message": "Forbidden" }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.ApplyBranchRulesAsync(ValidBaseUrl, ValidSlug, "rules_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(BranchRuleWriteErrors.InsufficientPermissionCode);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenWriteIsRejectedByPlan_FailsWithProviderErrorCarryingProviderMessage()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.UnprocessableEntity, """{ "message": "Upgrade to GitHub Pro to enable this feature." }"""),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.ApplyBranchRulesAsync(ValidBaseUrl, ValidSlug, "rules_token", CancellationToken.None);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(GitHubErrors.ProviderError(string.Empty).Code);
        failure.Error.Message.ShouldContain("Upgrade to GitHub Pro");
    }

    [Fact]
    public async Task WhenBaseUrlIsNotHttps_FailsWithoutSendingRequests()
    {
        // Arrange
        SequentialFakeHandler handler = new(Array.Empty<(HttpStatusCode, string)>());
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = CreateSut(httpClient);

        // Act
        Result result = await sut.ApplyBranchRulesAsync(
            new Uri("http://api.github.com"),
            ValidSlug,
            "rules_token",
            CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.ShouldBe(GitHubErrors.InvalidBaseUrl);
        handler.Requests.ShouldBeEmpty();
    }
}
