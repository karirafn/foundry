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

public sealed class WhenSettingsDoNotExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenSettingsDoNotExist()
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
    public async Task ReturnsNotFoundAsProblemDetails()
    {
        // Arrange — no settings seeded
        object body = new UpdateWorkerEnvironmentRequestBuilder().Build();

        // Act
        HttpResponseMessage response = await PutAsync(body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Type.ShouldEndWith("Settings.NotFound");
    }
}
