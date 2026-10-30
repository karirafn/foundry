using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Testing;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.GetUsageSnapshotTests;

public sealed class HandleAsync
{
    // ---------------------------------------------------------------------------
    // Behavior (a): snapshot present → 200 OK with DTO
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenSnapshotExists_ReturnsOkWithMappedDto()
    {
        // Arrange
        UsageSnapshot snapshot = new UsageSnapshotBuilder()
            .WithAccountEmail("user@example.com")
            .WithCapturedAt(new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero))
            .Build();

        UsageSnapshotState state = new();
        ((IUsageSnapshotStateMutator)state).SetSnapshot(snapshot);

        // Act
        IResult result = GetUsageSnapshot.Endpoint.Handle(state);

        // Assert
        Ok<UsageSnapshotResponse> ok = result.ShouldBeOfType<Ok<UsageSnapshotResponse>>();
        UsageSnapshotResponse dto = ok.Value.ShouldNotBeNull();
        dto.ShouldSatisfyAllConditions(
            () => dto.AccountEmail.ShouldBe("user@example.com"),
            () => dto.CapturedAt.ShouldBe(new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero)),
            () => dto.Limits.ShouldNotBeEmpty(),
            () => dto.LastRefreshFailedAt.ShouldBeNull());
    }

    // ---------------------------------------------------------------------------
    // Behavior (b): no snapshot → 204 No Content
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenNoSnapshot_ReturnsNoContent()
    {
        // Arrange
        UsageSnapshotState state = new();

        // Act
        IResult result = GetUsageSnapshot.Endpoint.Handle(state);

        // Assert
        result.ShouldBeOfType<NoContent>();
    }

    // ---------------------------------------------------------------------------
    // Behavior (c): severity serializes as a string (present in DTO as string, not int)
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenSnapshotExists_SeverityIsString()
    {
        // Arrange
        UsageSnapshotState state = new();
        UsageSnapshot snapshot = new UsageSnapshotBuilder().Build();
        ((IUsageSnapshotStateMutator)state).SetSnapshot(snapshot);

        // Act
        IResult result = GetUsageSnapshot.Endpoint.Handle(state);

        // Assert
        Ok<UsageSnapshotResponse> ok = result.ShouldBeOfType<Ok<UsageSnapshotResponse>>();
        UsageSnapshotResponse dto = ok.Value.ShouldNotBeNull();
        dto.Limits.ShouldNotBeEmpty();
        UsageRateLimitResponse first = dto.Limits[0];
        first.Severity.ShouldBe("normal"); // string, not numeric
    }

    // ---------------------------------------------------------------------------
    // Behavior (d): lastRefreshFailedAt is present (ISO-8601) when set
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhenLastRefreshFailedAtIsSet_DtoCarriesIt()
    {
        // Arrange
        DateTimeOffset failedAt = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);
        UsageSnapshotState state = new();
        UsageSnapshot snapshot = new UsageSnapshotBuilder()
            .WithLastRefreshFailedAt(failedAt)
            .Build();
        ((IUsageSnapshotStateMutator)state).SetSnapshot(snapshot);

        // Stamp the failure so LastRefreshFailedAt propagates
        ((IUsageSnapshotStateMutator)state).MarkRefreshFailed(failedAt);

        // Act
        IResult result = GetUsageSnapshot.Endpoint.Handle(state);

        // Assert
        Ok<UsageSnapshotResponse> ok = result.ShouldBeOfType<Ok<UsageSnapshotResponse>>();
        UsageSnapshotResponse dto = ok.Value.ShouldNotBeNull();
        dto.LastRefreshFailedAt.ShouldBe(failedAt);
    }
}
