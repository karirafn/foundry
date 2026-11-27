using System.Net;

using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class ListRepositoriesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private const string SelfPath = "personal_access_tokens/self";

    private static GitLabHttpClient CreateSut(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static string SelfJson(params string[] entries) =>
        $$"""{ "scopes": ["granular"], "expires_at": null, "granular_scopes": [{{string.Join(",", entries)}}] }""";

    private static string ProjectEntry(int projectId) =>
        $$"""{ "access": "selected_memberships", "permissions": ["read_project"], "project_id": {{projectId}}, "group_id": null }""";

    private const string UserEntry =
        """{ "access": "user", "permissions": ["read_personal_access_token", "read_user"], "project_id": null, "group_id": null }""";

    private const string GroupEntry =
        """{ "access": "selected_memberships", "permissions": ["read_package"], "project_id": null, "group_id": 9 }""";

    private static string ProjectJson(string path, string visibility, int projectLevel, int? groupLevel = null) =>
        $$"""
        {
          "id": 1,
          "path_with_namespace": "{{path}}",
          "visibility": "{{visibility}}",
          "permissions": {
            "project_access": { "access_level": {{projectLevel}} },
            "group_access": {{(groupLevel is null ? "null" : $$"""{ "access_level": {{groupLevel}} }""")}}
          }
        }
        """;

    private static async Task<IReadOnlyList<ProviderRepository>> ListAsync(FakeHandler handler) =>
        (await CreateSut(handler).ListRepositoriesAsync(ValidBaseUrl, "glpat_token123", CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>().Value;

    [Fact]
    public async Task WhenTheGrantSelectsProjects_ListsExactlyThoseProjects()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(SelfPath, HttpStatusCode.OK, SelfJson(ProjectEntry(11), ProjectEntry(12), UserEntry, GroupEntry))
            .WithRoute("projects/11", HttpStatusCode.OK, ProjectJson("alice/public-project", "public", 40))
            .WithRoute("projects/12", HttpStatusCode.OK, ProjectJson("alice/secret-project", "private", 20));

        // Act
        IReadOnlyList<ProviderRepository> repos = await ListAsync(handler);

        // Assert
        repos.ShouldBe(
        [
            new ProviderRepository("alice/public-project", IsPrivate: false, CanPush: true),
            new ProviderRepository("alice/secret-project", IsPrivate: true, CanPush: false),
        ]);
    }

    [Fact]
    public async Task WhenTheGrantSelectsProjects_ReadsEachProjectByIdAndNeverListsMemberships()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(SelfPath, HttpStatusCode.OK, SelfJson(ProjectEntry(11), UserEntry))
            .WithRoute("projects/11", HttpStatusCode.OK, ProjectJson("org/repo", "public", 40));

        // Act
        await ListAsync(handler);

        // Assert
        handler.AllRequests.Select(r => r.RequestUri!.PathAndQuery).ShouldBe(
            ["/api/v4/personal_access_tokens/self", "/api/v4/projects/11"]);
    }

    [Theory]
    [InlineData(30, null, true)]
    [InlineData(20, null, false)]
    [InlineData(10, 30, true)]
    public async Task CanPush_IsTrueFromDeveloperOnTheProjectOrItsGroup(int projectLevel, int? groupLevel, bool expected)
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(SelfPath, HttpStatusCode.OK, SelfJson(ProjectEntry(11), UserEntry))
            .WithRoute("projects/11", HttpStatusCode.OK, ProjectJson("org/repo", "public", projectLevel, groupLevel));

        // Act
        IReadOnlyList<ProviderRepository> repos = await ListAsync(handler);

        // Assert
        repos.Single().CanPush.ShouldBe(expected);
    }

    [Fact]
    public async Task WhenTheTokenReportsNoGrant_ListsNothing()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(SelfPath, HttpStatusCode.OK, """{ "scopes": ["api"], "expires_at": null }""");

        // Act
        IReadOnlyList<ProviderRepository> repos = await ListAsync(handler);

        // Assert
        repos.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenCalled_UsesPrivateTokenHeader()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(SelfPath, HttpStatusCode.OK, SelfJson(UserEntry));

        // Act
        await ListAsync(handler);

        // Assert
        handler.LastRequest!.Headers.GetValues("PRIVATE-TOKEN").ShouldBe(["glpat_token123"]);
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await CreateSut(handler).ListRepositoriesAsync(
            new Uri("http://gitlab.com/api/v4"),
            "glpat_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenAProjectReadFails_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(SelfPath, HttpStatusCode.OK, SelfJson(ProjectEntry(11), UserEntry))
            .WithRoute("projects/11", HttpStatusCode.InternalServerError, string.Empty);

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await CreateSut(handler).ListRepositoriesAsync(
            ValidBaseUrl,
            "glpat_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
    }

    private const string AllMembershipsEntry =
        """{ "access": "all_memberships", "permissions": ["read_project", "read_package"], "project_id": null, "group_id": null }""";

    private static string ProjectListJson(IEnumerable<string> projects) => $"[{string.Join(",", projects)}]";

    [Fact]
    public async Task WhenTheGrantReachesAllMemberships_ListsTheMemberProjects()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(SelfPath, HttpStatusCode.OK, SelfJson(AllMembershipsEntry, UserEntry))
            .WithRoute("projects", HttpStatusCode.OK, ProjectListJson(
            [
                ProjectJson("alice/public-project", "public", 40),
                ProjectJson("org/secret-project", "private", 10, groupLevel: 30),
            ]));

        // Act
        IReadOnlyList<ProviderRepository> repos = await ListAsync(handler);

        // Assert
        repos.ShouldBe(
        [
            new ProviderRepository("alice/public-project", IsPrivate: false, CanPush: true),
            new ProviderRepository("org/secret-project", IsPrivate: true, CanPush: true),
        ]);
    }

    [Fact]
    public async Task WhenTheGrantReachesAllMembershipsAndSelectsProjects_ListsMembershipsInsteadOfReadingEachProject()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(SelfPath, HttpStatusCode.OK, SelfJson(ProjectEntry(11), AllMembershipsEntry, UserEntry))
            .WithRoute("projects", HttpStatusCode.OK, ProjectListJson([ProjectJson("org/repo", "public", 40)]));

        // Act
        await ListAsync(handler);

        // Assert
        handler.AllRequests.Select(r => r.RequestUri!.PathAndQuery).ShouldBe(
            ["/api/v4/personal_access_tokens/self", "/api/v4/projects?membership=true&per_page=100&page=1"]);
    }

    [Fact]
    public async Task WhenTheGrantReachesAllMembershipsAndAPageIsFull_ReadsTheNextPage()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, SelfJson(AllMembershipsEntry, UserEntry)),
            (HttpStatusCode.OK, ProjectListJson(Enumerable.Range(1, 100).Select(i => ProjectJson($"org/repo-{i}", "public", 40)))),
            (HttpStatusCode.OK, ProjectListJson([ProjectJson("org/last", "public", 40)])),
        ]);

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await CreateSut(handler).ListRepositoriesAsync(
            ValidBaseUrl,
            "glpat_token123",
            CancellationToken.None);

        // Assert
        IReadOnlyList<ProviderRepository> repos = result
            .ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>().Value;
        repos.Count.ShouldBe(101);
        repos[^1].Slug.ShouldBe("org/last");
        handler.Requests.Select(r => r.RequestUri!.PathAndQuery).ShouldBe(
        [
            "/api/v4/personal_access_tokens/self",
            "/api/v4/projects?membership=true&per_page=100&page=1",
            "/api/v4/projects?membership=true&per_page=100&page=2",
        ]);
    }

    [Fact]
    public async Task WhenTheGrantReachesAllMembershipsAndTheListingFails_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(SelfPath, HttpStatusCode.OK, SelfJson(AllMembershipsEntry, UserEntry))
            .WithRoute("projects", HttpStatusCode.Forbidden, """{ "error": "insufficient_granular_scope" }""");

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await CreateSut(handler).ListRepositoriesAsync(
            ValidBaseUrl,
            "glpat_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
    }
}
