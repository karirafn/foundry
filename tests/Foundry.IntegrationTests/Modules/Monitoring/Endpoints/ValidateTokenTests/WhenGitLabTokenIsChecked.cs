using System.Net;
using System.Net.Http.Json;
using System.Text;

using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.ValidateTokenTests;

/// <summary>
/// How <c>POST /api/accounts/validate-token</c> reads a GitLab token from what
/// <c>GET /personal_access_tokens/self</c> answers. The 403 body and the <c>granular_scopes</c> shape were
/// captured from gitlab.com on 2026-11-24.
/// </summary>
public sealed class WhenGitLabTokenIsChecked
{
    private const string UserJson = """{"username":"testuser"}""";

    private const string ConformingProjectPermissions =
        """["read_project","read_work_item","create_work_item","read_merge_request","create_merge_request","update_merge_request","merge_merge_request","read_pipeline","read_job","run_job","read_webhook","create_webhook","update_webhook","delete_webhook","trigger_webhook","read_branch","create_branch","delete_branch","protect_branch","read_protected_branch","update_protected_branch","read_repository","read_commit","download_code","push_code"]""";

    private const string UserEntry =
        """{"access":"user","permissions":["read_personal_access_token","read_user"],"project_id":null,"group_id":null}""";

    private static string ProjectEntry(string permissions) =>
        $$"""{"access":"selected_memberships","permissions":{{permissions}},"project_id":42,"group_id":null}""";

    private static string GrantJson(params string[] entries) =>
        $$"""{"scopes":["granular"],"expires_at":null,"granular_scopes":[{{string.Join(",", entries)}}]}""";

    private static async Task<(HttpStatusCode Status, ValidateToken.Response? Body)> ValidateAsync(
        HttpStatusCode selfStatus,
        string selfBody)
    {
        using SelfHandler gitLab = new(selfStatus, selfBody);
        await using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
            services.AddHttpClient<GitLabHttpClient>().ConfigurePrimaryHttpMessageHandler(() => gitLab));
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
            new { token = "glpat_test", baseUrl = "https://gitlab.com", providerType = "gitlab" },
            TestContext.Current.CancellationToken);

        ValidateToken.Response? body = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ValidateToken.Response>(TestContext.Current.CancellationToken)
            : null;
        return (response.StatusCode, body);
    }

    [Fact]
    public async Task WhenSelfIsRefusedForInsufficientGranularScope_IsAuthenticatedMissingPersonalAccessTokenRead()
    {
        // Arrange

        // Act
        (HttpStatusCode status, ValidateToken.Response? body) = await ValidateAsync(
            HttpStatusCode.Forbidden,
            """{"error":"insufficient_granular_scope"}""");

        // Assert
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.ShouldSatisfyAllConditions(
            () => body.Kind.ShouldBe(ValidateToken.Kinds.Authenticated),
            () => body.MissingScopes.ShouldBe(["Personal Access Token (System Access): Read"]));
    }

    [Theory]
    [InlineData("""{"scopes":["granular"],"expires_at":null}""", "gitlab-version")]
    [InlineData("""{"scopes":["api"],"expires_at":null}""", "gitlab-legacy")]
    public async Task WhenSelfReportsNoGranularScopes_IsTheWrongTokenKind(string selfBody, string wrongKind)
    {
        // Arrange

        // Act
        (HttpStatusCode status, ValidateToken.Response? body) = await ValidateAsync(HttpStatusCode.OK, selfBody);

        // Assert
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.ShouldSatisfyAllConditions(
            () => body.Kind.ShouldBe(ValidateToken.Kinds.WrongTokenKind),
            () => body.WrongKind.ShouldBe(wrongKind));
    }

    [Fact]
    public async Task WhenSelfIsRefusedOtherwise_IsAGroupOrProjectAccessToken()
    {
        // Arrange

        // Act
        (HttpStatusCode status, ValidateToken.Response? body) = await ValidateAsync(
            HttpStatusCode.NotFound,
            """{"message":"404 Not Found"}""");

        // Assert
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.ShouldSatisfyAllConditions(
            () => body.Kind.ShouldBe(ValidateToken.Kinds.WrongTokenKind),
            () => body.WrongKind.ShouldBe("gitlab-resource-token"));
    }

    [Fact]
    public async Task WhenTheGrantConforms_IsAuthenticatedWithNothingToFix()
    {
        // Arrange

        // Act
        (HttpStatusCode status, ValidateToken.Response? body) = await ValidateAsync(
            HttpStatusCode.OK,
            GrantJson(ProjectEntry(ConformingProjectPermissions), UserEntry));

        // Assert
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.ShouldSatisfyAllConditions(
            () => body.Kind.ShouldBe(ValidateToken.Kinds.Authenticated),
            () => body.AccountName.ShouldBe("testuser"),
            () => body.MissingScopes.ShouldBeEmpty(),
            () => body.NotAllowed.ShouldBeEmpty(),
            () => body.BroadReach.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenTheGrantReachesAllGroupsAndProjectsWithPackageRead_IsAuthenticatedWithNothingToFix()
    {
        // Arrange
        // The all_memberships entry shape was captured from gitlab.com on 2026-11-27.
        string allMembershipsEntry =
            $$"""{"access":"all_memberships","permissions":{{ConformingProjectPermissions.Replace("]", ",\"read_package\"]", StringComparison.Ordinal)}},"project_id":null,"group_id":null}""";
        const string userEntryWithProjectRead =
            """{"access":"user","permissions":["read_personal_access_token","read_user","read_project"],"project_id":null,"group_id":null}""";

        // Act
        (HttpStatusCode status, ValidateToken.Response? body) = await ValidateAsync(
            HttpStatusCode.OK,
            GrantJson(allMembershipsEntry, userEntryWithProjectRead));

        // Assert
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.ShouldSatisfyAllConditions(
            () => body.Kind.ShouldBe(ValidateToken.Kinds.Authenticated),
            () => body.MissingScopes.ShouldBeEmpty(),
            () => body.NotAllowed.ShouldBeEmpty(),
            () => body.BroadReach.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenTheGrantBreaksEveryRule_ListsMissingNotAllowedAndBroadReach()
    {
        // Arrange
        string partialProject = ConformingProjectPermissions
            .Replace("\"trigger_webhook\"", "\"test_webhook\"", StringComparison.Ordinal);
        const string instanceEntry = """{"access":"instance","permissions":["read_project"],"project_id":null,"group_id":null}""";

        // Act
        (HttpStatusCode status, ValidateToken.Response? body) = await ValidateAsync(
            HttpStatusCode.OK,
            GrantJson(ProjectEntry(partialProject), UserEntry, instanceEntry));

        // Assert
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.ShouldSatisfyAllConditions(
            () => body.Kind.ShouldBe(ValidateToken.Kinds.Authenticated),
            () => body.MissingScopes.ShouldBe(["Webhook (Integrations): Trigger"]),
            () => body.NotAllowed.ShouldBe(["test_webhook"]),
            () => body.BroadReach.ShouldBe(["instance"]));
    }

    // Answers /personal_access_tokens/self with the given status and body, and every other path as /user.
    private sealed class SelfHandler(HttpStatusCode selfStatus, string selfBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            bool isSelf = request.RequestUri!.AbsolutePath.EndsWith("personal_access_tokens/self", StringComparison.Ordinal);
            HttpResponseMessage response = new(isSelf ? selfStatus : HttpStatusCode.OK)
            {
                Content = new StringContent(isSelf ? selfBody : UserJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
