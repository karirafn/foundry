using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Testing;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateClaudeCodeSettingsTests;

public sealed class WhenValid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenValid()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedDefaultSettingsAsync()
    {
        // SettingsSeeder is a hosted service and is removed in tests — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        dbContext.Set<GlobalSettings>().Add(GlobalSettings.Create());
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private Task<HttpResponseMessage> PutAsync(object body) =>
        _client.PutAsJsonAsync(
            new Uri("/api/settings/claude-code", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReturnsOkWithUpdatedClaudeCodeSettings()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new UpdateClaudeCodeSettingsRequestBuilder()
            .WithModel("claude-opus-4-1")
            .WithBlockCiCdEdits(false)
            .WithAdditionalDenyRule("Bash(rm:*)")
            .Build();

        // Act
        HttpResponseMessage response = await PutAsync(body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary summary = (await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.Model.ShouldBe("claude-opus-4-1"),
            () => summary.BlockCiCdEdits.ShouldBeFalse(),
            () => summary.AdditionalDenyRules.ShouldBe(["Bash(rm:*)"]));
    }

    [Fact]
    public async Task PersistsClaudeCodeSettings()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new UpdateClaudeCodeSettingsRequestBuilder()
            .WithModel("claude-opus-4-1")
            .WithAdditionalDenyRule("Read(./secrets/**)")
            .Build();

        // Act
        await PutAsync(body);

        // Assert
        HttpResponseMessage getResponse = await _client.GetAsync(
            new Uri("/api/settings", UriKind.Relative),
            TestContext.Current.CancellationToken);
        GlobalSettingsSummary summary = (await getResponse.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.Model.ShouldBe("claude-opus-4-1"),
            () => summary.AdditionalDenyRules.ShouldBe(["Read(./secrets/**)"]));
    }
}
