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

public sealed class WhenRequestInvalid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRequestInvalid()
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
    public async Task WhenAdditionalDenyRuleMalformed_ReturnsBadRequest()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new UpdateClaudeCodeSettingsRequestBuilder()
            .WithAdditionalDenyRule("not-a-rule")
            .Build();

        // Act
        HttpResponseMessage response = await PutAsync(body);

        // Assert
        await AssertProblemAsync(response, "Settings.InvalidDenyRule");
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, string expectedErrorCode)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Type.ShouldEndWith(expectedErrorCode);
    }
}
