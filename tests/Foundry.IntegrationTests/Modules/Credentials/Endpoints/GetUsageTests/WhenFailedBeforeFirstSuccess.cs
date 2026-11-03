using System.Net;
using System.Text.Json;

using Foundry.Modules.Credentials.Features.Usage;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.GetUsageTests;

/// <summary>
/// Integration tests for <c>GET /api/credentials/usage</c> when a poll has failed before any
/// poll succeeded. The response must be distinguishable from 204 ("nothing to report").
/// </summary>
public sealed class WhenFailedBeforeFirstSuccess : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenFailedBeforeFirstSuccess()
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
    public async Task Returns200WithNullCapturedAtAndFailureStamp()
    {
        // Arrange
        DateTimeOffset failedAt = new(2025, 1, 15, 13, 0, 0, TimeSpan.Zero);
        IUsageSnapshotStateMutator mutator = _factory.Services.GetRequiredService<IUsageSnapshotStateMutator>();
        mutator.MarkRefreshFailed("user@example.com", failedAt);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials/usage", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        string json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        root.ShouldSatisfyAllConditions(
            () => root.GetProperty("capturedAt").ValueKind.ShouldBe(JsonValueKind.Null),
            () => root.GetProperty("limits").GetArrayLength().ShouldBe(0),
            () => root.GetProperty("extraUsage").ValueKind.ShouldBe(JsonValueKind.Null),
            () => root.GetProperty("accountEmail").GetString().ShouldBe("user@example.com"),
            () => root.GetProperty("lastRefreshFailedAt").GetDateTimeOffset().ShouldBe(failedAt));
    }
}
