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
            () => dto.Scopes.ShouldBe([
                "Contents (read and write)",
                "Issues (read and write)",
                "Pull requests (read and write)",
                "Workflows (write)",
                "Metadata (read)"]),
            () => dto.CreationUrlTemplate.ShouldContain("personal-access-tokens/new"),
            () => dto.CreationUrlTemplate.ShouldContain("name=Foundry&contents=write&issues=write&pull_requests=write&workflows=write"),
            () => dto.ResourceOwnerHint.ShouldNotBeNull());
    }
}
