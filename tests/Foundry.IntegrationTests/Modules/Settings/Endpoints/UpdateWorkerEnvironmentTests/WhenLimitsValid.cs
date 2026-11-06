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

public sealed class WhenLimitsValid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenLimitsValid()
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
    public async Task ReturnsOkWithUpdatedLimits()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new UpdateWorkerEnvironmentRequestBuilder()
            .WithMemoryLimitMb(16384)
            .WithCpuLimit(6.5)
            .WithPidsLimit(2048)
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
            () => summary.MemoryLimitMb.ShouldBe(16384),
            () => summary.CpuLimit.ShouldBe(6.5),
            () => summary.PidsLimit.ShouldBe(2048));
    }

    [Fact]
    public async Task PersistsLimits()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new UpdateWorkerEnvironmentRequestBuilder()
            .WithMemoryLimitMb(16384)
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
        summary.MemoryLimitMb.ShouldBe(16384);
    }
}
