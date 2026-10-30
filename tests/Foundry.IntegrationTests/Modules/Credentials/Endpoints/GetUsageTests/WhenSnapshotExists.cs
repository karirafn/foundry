using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Testing;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.GetUsageTests;

/// <summary>
/// Integration tests for <c>GET /api/credentials/usage</c> when a snapshot is present.
/// Verifies the real STJ camelCase JSON shape — this is the authoritative wire contract
/// that the frontend TypeScript spec (step 10) mirrors.
/// </summary>
public sealed class WhenSnapshotExists : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenSnapshotExists()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private void SeedSnapshot(UsageSnapshot snapshot)
    {
        // Seed directly into the singleton via the mutator interface — no endpoint exists to produce
        // a snapshot (the poller would require Docker); use DI directly.
        IUsageSnapshotStateMutator mutator = _factory.Services.GetRequiredService<IUsageSnapshotStateMutator>();
        mutator.SetSnapshot(snapshot);
    }

    [Fact]
    public async Task Returns200WithCamelCaseJson()
    {
        // Arrange
        DateTimeOffset capturedAt = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset resetsAt = new DateTimeOffset(2025, 1, 15, 17, 0, 0, TimeSpan.Zero);

        UsageSnapshot snapshot = new UsageSnapshotBuilder()
            .WithAccountEmail("user@example.com")
            .WithCapturedAt(capturedAt)
            .WithLimits(
            [
                new UsageRateLimit(
                    Kind: "session",
                    Percent: 42.5m,
                    ResetsAt: resetsAt,
                    ModelDisplayName: null,
                    Severity: "normal",
                    IsActive: true),
            ])
            .Build();

        SeedSnapshot(snapshot);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials/usage", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — status
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert — wire shape via raw JsonDocument to verify camelCase names exactly
        // (This is the authoritative contract the frontend TS spec mirrors — step 10)
        string json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Top-level camelCase fields
        root.GetProperty("accountEmail").GetString().ShouldBe("user@example.com");
        root.GetProperty("capturedAt").GetDateTimeOffset().ShouldBe(capturedAt);
        root.GetProperty("lastRefreshFailedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        // limits array
        JsonElement limits = root.GetProperty("limits");
        limits.GetArrayLength().ShouldBe(1);

        JsonElement limit = limits[0];
        limit.GetProperty("kind").GetString().ShouldBe("session");
        limit.GetProperty("percent").GetDecimal().ShouldBe(42.5m);
        limit.GetProperty("resetsAt").GetDateTimeOffset().ShouldBe(resetsAt);
        limit.GetProperty("modelDisplayName").ValueKind.ShouldBe(JsonValueKind.Null);

        // severity is a string, never a numeric enum
        JsonElement severityElement = limit.GetProperty("severity");
        severityElement.ValueKind.ShouldBe(JsonValueKind.String);
        severityElement.GetString().ShouldBe("normal");

        limit.GetProperty("isActive").GetBoolean().ShouldBeTrue();

        // extraUsage is null when not set
        root.GetProperty("extraUsage").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task WhenLastRefreshFailedAtIsSet_IncludesItInResponse()
    {
        // Arrange
        DateTimeOffset capturedAt = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset failedAt = new DateTimeOffset(2025, 1, 15, 13, 0, 0, TimeSpan.Zero);

        UsageSnapshot snapshot = new UsageSnapshotBuilder()
            .WithCapturedAt(capturedAt)
            .Build();

        IUsageSnapshotStateMutator mutator = _factory.Services.GetRequiredService<IUsageSnapshotStateMutator>();
        mutator.SetSnapshot(snapshot);
        mutator.MarkRefreshFailed(failedAt);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials/usage", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        string json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // lastRefreshFailedAt is present and ISO-8601
        JsonElement lastRefreshFailedAt = root.GetProperty("lastRefreshFailedAt");
        lastRefreshFailedAt.ValueKind.ShouldBe(JsonValueKind.String);
        lastRefreshFailedAt.GetDateTimeOffset().ShouldBe(failedAt);
    }

    [Fact]
    public async Task WhenExtraUsageIsSet_IncludesItInResponse()
    {
        // Arrange
        UsageExtraCredits extra = new(
            IsEnabled: true,
            MonthlyLimit: 50000L,
            UsedCredits: 16568L,
            Utilization: 0.33136m,
            Currency: "USD");

        UsageSnapshot snapshot = new UsageSnapshotBuilder()
            .WithExtraUsage(extra)
            .Build();

        SeedSnapshot(snapshot);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/credentials/usage", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        string json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        JsonElement extraUsage = root.GetProperty("extraUsage");
        extraUsage.ValueKind.ShouldNotBe(JsonValueKind.Null);
        extraUsage.GetProperty("isEnabled").GetBoolean().ShouldBeTrue();
        extraUsage.GetProperty("monthlyLimit").GetInt64().ShouldBe(50000L);
        extraUsage.GetProperty("usedCredits").GetInt64().ShouldBe(16568L);
        extraUsage.GetProperty("currency").GetString().ShouldBe("USD");
    }
}
