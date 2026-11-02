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

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateWorkerEnvironmentTests;

public sealed class WhenHostHooksValid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenHostHooksValid()
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
            new Uri("/api/settings/worker-environment", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReturnsOkWithCanonicalPathAndDeduplicatedExclusions()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new UpdateWorkerEnvironmentRequestBuilder()
            .WithHostSettingsPath(" /home/user/.claude/settings.json ")
            .WithExcludedHookCommands(" bash ~/.claude/hooks/windows-check.sh ", "bash ~/.claude/hooks/windows-check.sh", "notify")
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
            () => summary.HostSettingsPath.ShouldBe("/home/user/.claude/settings.json"),
            () => summary.ExcludedHookCommands.ShouldBe(["bash ~/.claude/hooks/windows-check.sh", "notify"]));
    }

    [Fact]
    public async Task AcceptsWindowsHostSettingsPathAndPersistsIt()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new UpdateWorkerEnvironmentRequestBuilder()
            .WithHostSettingsPath(@"C:\Users\me\.claude\settings.json")
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
        summary.HostSettingsPath.ShouldBe(@"C:\Users\me\.claude\settings.json");
    }

    [Fact]
    public async Task WhenHostHooksFieldsOmitted_ClearsHostHooks()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        await PutAsync(new UpdateWorkerEnvironmentRequestBuilder()
            .WithHostSettingsPath("/home/user/.claude/settings.json")
            .WithExcludedHookCommands("notify")
            .Build());

        // Act
        HttpResponseMessage response = await PutAsync(new { memoryLimitMb = 4096, cpuLimit = 4.0, pidsLimit = 1024, mounts = Array.Empty<object>() });

        // Assert
        GlobalSettingsSummary summary = (await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.HostSettingsPath.ShouldBeEmpty(),
            () => summary.ExcludedHookCommands.ShouldBeEmpty());
    }
}
