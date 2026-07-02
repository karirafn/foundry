using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.GetLoginCommandTests;

public sealed class WhenRequested : IAsyncDisposable
{
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
    public async Task ReturnsOk()
    {
        // Arrange — no settings required; command is built from constants

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings/oauth/login-command", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReturnsCommandContainingCredentialVolumeName()
    {
        // Arrange

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings/oauth/login-command", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        OAuthLoginCommand? dto = await response.Content
            .ReadFromJsonAsync<OAuthLoginCommand>(TestContext.Current.CancellationToken);
        dto.ShouldNotBeNull();
        dto.Command.ShouldContain(WorkerVolumeNames.CredentialVolumeName);
    }

    [Fact]
    public async Task ReturnsCommandContainingLoginImageName()
    {
        // Arrange

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings/oauth/login-command", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        OAuthLoginCommand? dto = await response.Content
            .ReadFromJsonAsync<OAuthLoginCommand>(TestContext.Current.CancellationToken);
        dto.ShouldNotBeNull();
        dto.Command.ShouldContain(WorkerImageNames.LoginImageName);
    }
}
