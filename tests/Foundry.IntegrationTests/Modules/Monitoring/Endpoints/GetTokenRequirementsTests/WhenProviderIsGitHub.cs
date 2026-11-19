using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetTokenRequirementsTests;

public sealed class WhenProviderIsGitHub : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenProviderIsGitHub()
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
    public async Task ReturnsOkWithRequirements()
    {
        // Arrange — no seeding required; catalog is static

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/providers/github/token-requirements", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        TokenRequirements? dto = await response.Content
            .ReadFromJsonAsync<TokenRequirements>(TestContext.Current.CancellationToken);
        dto.ShouldNotBeNull();
        dto.ShouldSatisfyAllConditions(
            () => dto.TokenTypeLabel.ShouldBe("GitHub fine-grained personal access token"),
            () => dto.Permissions.ShouldBe([
                new TokenPermissionRequirement("Contents (read and write)", "probe"),
                new TokenPermissionRequirement("Issues (read and write)", "probe"),
                new TokenPermissionRequirement("Pull requests (read and write)", "probe"),
                new TokenPermissionRequirement("Administration (read and write)", "probe"),
                new TokenPermissionRequirement("Webhooks (read and write)", "probe"),
                new TokenPermissionRequirement("Commit statuses (read)", "probe"),
                new TokenPermissionRequirement("Actions (read)", "probe"),
                new TokenPermissionRequirement("Metadata (read)", "always-granted"),
                new TokenPermissionRequirement("Workflows (read and write)", "unverifiable")]),
            () => dto.CreationUrlTemplate.ShouldContain("personal-access-tokens/new"),
            () => dto.CreationUrlTemplate.ShouldContain("name=Foundry&contents=write&issues=write&pull_requests=write&administration=write&repository_hooks=write&statuses=read&actions=read&workflows=write"),
            () => dto.ResourceOwnerHint.ShouldNotBeNull());
    }
}
