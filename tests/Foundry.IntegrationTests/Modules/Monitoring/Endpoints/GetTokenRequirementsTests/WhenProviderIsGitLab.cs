using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetTokenRequirementsTests;

public sealed class WhenProviderIsGitLab : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenProviderIsGitLab()
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
            new Uri("/api/providers/gitlab/token-requirements", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        TokenRequirements? dto = await response.Content
            .ReadFromJsonAsync<TokenRequirements>(TestContext.Current.CancellationToken);
        dto.ShouldNotBeNull();
        dto.ShouldSatisfyAllConditions(
            () => dto.Scopes.ShouldBe(["api"]),
            () => dto.CreationUrlTemplate.ShouldContain("/-/user_settings/personal_access_tokens"));
    }
}
