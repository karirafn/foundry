using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateAwaitingInputSettingsTests;

public sealed class WhenTheReminderDelayIsSaved : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTheReminderDelayIsSaved()
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

    private async Task<HttpResponseMessage> PutAsync(int awaitingInputReminderDelayHours)
        => await _client.PutAsJsonAsync(
            new Uri("/api/settings/awaiting-input", UriKind.Relative),
            new { awaitingInputReminderDelayHours },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task APositiveDelay_IsSavedAndReturned()
    {
        // Arrange
        await SeedDefaultSettingsAsync();

        // Act
        HttpResponseMessage response = await PutAsync(awaitingInputReminderDelayHours: 24);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await _client.GetFromJsonAsync<GlobalSettingsSummary>(
            new Uri("/api/settings", UriKind.Relative),
            FoundryWebAppFactory.JsonOptions,
            TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull().AwaitingInputReminderDelayHours.ShouldBe(24);
    }

    [Fact]
    public async Task ByDefault_TheDelayIsThreeDays()
    {
        // Arrange
        await SeedDefaultSettingsAsync();

        // Act
        GlobalSettingsSummary? summary = await _client.GetFromJsonAsync<GlobalSettingsSummary>(
            new Uri("/api/settings", UriKind.Relative),
            FoundryWebAppFactory.JsonOptions,
            TestContext.Current.CancellationToken);

        // Assert
        summary.ShouldNotBeNull().AwaitingInputReminderDelayHours.ShouldBe(72);
    }

    [Fact]
    public async Task AZeroDelay_IsABadRequest()
    {
        // Arrange
        await SeedDefaultSettingsAsync();

        // Act
        HttpResponseMessage response = await PutAsync(awaitingInputReminderDelayHours: 0);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonNode node = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotBeNull();
        node["type"].ShouldNotBeNull().GetValue<string>().ShouldEndWith("Settings.InvalidAwaitingInputReminderDelay");
    }
}
