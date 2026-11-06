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

    [Fact]
    public void WhenFailedBeforeFirstSuccess_ReturnsOkWithUnavailableDto()
    {
        // Arrange
        UsageSnapshotState state = new();
        DateTimeOffset failedAt = new(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);
        ((IUsageSnapshotStateMutator)state).MarkRefreshFailed("user@example.com", failedAt);

        // Act
        IResult result = GetUsageSnapshot.Endpoint.Handle(state);

        // Assert — distinguishable from 204: 200 with null capturedAt and the failure stamp
        Ok<UsageSnapshotResponse> ok = result.ShouldBeOfType<Ok<UsageSnapshotResponse>>();
        UsageSnapshotResponse dto = ok.Value.ShouldNotBeNull();
        dto.ShouldSatisfyAllConditions(
            () => dto.AccountEmail.ShouldBe("user@example.com"),
            () => dto.CapturedAt.ShouldBeNull(),
            () => dto.Limits.ShouldBeEmpty(),
            () => dto.ExtraUsage.ShouldBeNull(),
            () => dto.LastRefreshFailedAt.ShouldBe(failedAt));
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
        ((IUsageSnapshotStateMutator)state).MarkRefreshFailed(snapshot.AccountEmail, failedAt);

        // Act
        IResult result = GetUsageSnapshot.Endpoint.Handle(state);

        // Assert
        Ok<UsageSnapshotResponse> ok = result.ShouldBeOfType<Ok<UsageSnapshotResponse>>();
        UsageSnapshotResponse dto = ok.Value.ShouldNotBeNull();
        dto.LastRefreshFailedAt.ShouldBe(failedAt);
    }

    // ---------------------------------------------------------------------------
    // Source and extra-usage credits
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(UsageSnapshotSource.UsageReport), "usageReport")]
    [InlineData(nameof(UsageSnapshotSource.ProbeTurn), "probeTurn")]
    public void WhenSnapshotExists_DtoCarriesSourceAsCamelCaseString(string sourceName, string expected)
    {
        // Arrange
        UsageSnapshotSource source = Enum.Parse<UsageSnapshotSource>(sourceName);
        UsageSnapshotState state = new();
        ((IUsageSnapshotStateMutator)state).SetSnapshot(new UsageSnapshotBuilder().WithSource(source).Build());

        // Act
        IResult result = GetUsageSnapshot.Endpoint.Handle(state);

        // Assert
        UsageSnapshotResponse dto = result.ShouldBeOfType<Ok<UsageSnapshotResponse>>().Value.ShouldNotBeNull();
        dto.Source.ShouldBe(expected);
    }

    [Fact]
    public void WhenFailedBeforeFirstSuccess_DtoSourceIsNull()
    {
        // Arrange
        UsageSnapshotState state = new();
        ((IUsageSnapshotStateMutator)state).MarkRefreshFailed("user@example.com", DateTimeOffset.UnixEpoch);

        // Act
        IResult result = GetUsageSnapshot.Endpoint.Handle(state);

        // Assert
        UsageSnapshotResponse dto = result.ShouldBeOfType<Ok<UsageSnapshotResponse>>().Value.ShouldNotBeNull();
        dto.Source.ShouldBeNull();
    }

    [Fact]
    public void WhenExtraUsageHasALimit_DtoCarriesServerComputedPercentAndSeverity()
    {
        // Arrange
        UsageSnapshotState state = new();
        UsageSnapshot snapshot = new UsageSnapshotBuilder()
            .WithExtraUsage(new UsageExtraCredits(
                IsEnabled: true,
                MonthlyLimit: 50000,
                UsedCredits: 45000,
                Utilization: null,
                Currency: "USD"))
            .Build();
        ((IUsageSnapshotStateMutator)state).SetSnapshot(snapshot);

        // Act
        IResult result = GetUsageSnapshot.Endpoint.Handle(state);

        // Assert
        UsageSnapshotResponse dto = result.ShouldBeOfType<Ok<UsageSnapshotResponse>>().Value.ShouldNotBeNull();
        UsageExtraCreditsResponse extra = dto.ExtraUsage.ShouldNotBeNull();
        extra.ShouldSatisfyAllConditions(
            () => extra.MonthlyLimit.ShouldBe(50000),
            () => extra.UsedCredits.ShouldBe(45000),
            () => extra.Utilization.ShouldBeNull(),
            () => extra.Percent.ShouldBe(90m),
            () => extra.Severity.ShouldBe("warning"));
    }

    [Fact]
    public void WhenExtraUsageHasNoLimit_DtoPercentAndSeverityAreNull()
    {
        // Arrange
        UsageSnapshotState state = new();
        UsageSnapshot snapshot = new UsageSnapshotBuilder()
            .WithExtraUsage(new UsageExtraCredits(
                IsEnabled: true,
                MonthlyLimit: null,
                UsedCredits: 1200,
                Utilization: null,
                Currency: "USD"))
            .Build();
        ((IUsageSnapshotStateMutator)state).SetSnapshot(snapshot);

        // Act
        IResult result = GetUsageSnapshot.Endpoint.Handle(state);

        // Assert
        UsageExtraCreditsResponse extra = result.ShouldBeOfType<Ok<UsageSnapshotResponse>>()
            .Value.ShouldNotBeNull().ExtraUsage.ShouldNotBeNull();
        extra.ShouldSatisfyAllConditions(
            () => extra.MonthlyLimit.ShouldBeNull(),
            () => extra.Percent.ShouldBeNull(),
            () => extra.Severity.ShouldBeNull());
    }
}
