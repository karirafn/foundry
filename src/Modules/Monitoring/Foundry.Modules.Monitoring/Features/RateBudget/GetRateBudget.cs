using System.Diagnostics;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Monitoring.Features.RateBudget;

/// <summary>
/// Snapshot of all provider rate-budget headroom entries.
/// Always contains exactly three entries in a deterministic order: GitHubRest, GitHubGraphQl, GitLabRest.
/// </summary>
internal sealed record RateBudgetSnapshot(IReadOnlyList<ProviderBudgetHeadroom> Budgets);

/// <summary>
/// Per-budget headroom entry surfaced by <see cref="GetRateBudget"/>.
/// </summary>
internal sealed record ProviderBudgetHeadroom(
    string Budget,
    string DisplayName,
    int? Remaining,
    int? Limit,
    DateTimeOffset? ResetAt,
    DateTimeOffset? ObservedAt,
    int? Floor,
    string? Health);

/// <summary>
/// Maps <see cref="IProviderRateBudget"/> readings into a <see cref="RateBudgetSnapshot"/> response.
/// Pure function — no side effects; all inputs are parameters.
/// </summary>
internal static class GetRateBudgetMapper
{
    public static RateBudgetSnapshot Map(IProviderRateBudget store, DateTimeOffset now)
    {
        ProviderBudgetHeadroom[] budgets =
        [
            MapEntry(store, ProviderBudgetKey.GitHubRest, "GitHubRest", "GitHub REST", now, evaluateHealth: true),
            MapEntry(store, ProviderBudgetKey.GitHubGraphQl, "GitHubGraphQl", "GitHub GraphQL", now, evaluateHealth: true),
            MapEntry(store, ProviderBudgetKey.GitLabRest, "GitLabRest", "GitLab REST", now, evaluateHealth: false),
        ];

        return new RateBudgetSnapshot(budgets);
    }

    private static ProviderBudgetHeadroom MapEntry(
        IProviderRateBudget store,
        ProviderBudgetKey key,
        string budget,
        string displayName,
        DateTimeOffset now,
        bool evaluateHealth)
    {
        RateBudgetReading? reading = store.TryGet(key);

        int? floor = evaluateHealth ? ProviderBudgetPolicy.DefaultFloor : null;
        string? health = evaluateHealth ? HealthToString(ProviderBudgetPolicy.Evaluate(reading, ProviderBudgetPolicy.DefaultFloor, now)) : null;

        return new ProviderBudgetHeadroom(
            Budget: budget,
            DisplayName: displayName,
            Remaining: reading?.Remaining,
            Limit: reading?.Limit,
            ResetAt: reading?.ResetAt,
            ObservedAt: reading?.ObservedAt,
            Floor: floor,
            Health: health);
    }

    private static string HealthToString(ProviderBudgetHealth health) =>
        health switch
        {
            ProviderBudgetHealth.Healthy => "Healthy",
            ProviderBudgetHealth.Low => "Low",
            ProviderBudgetHealth.Unknown => "Unknown",
            _ => throw new UnreachableException($"Unhandled {nameof(ProviderBudgetHealth)}: {health}"),
        };
}

internal static class GetRateBudget
{
    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/", static (IProviderRateBudget store, TimeProvider timeProvider) =>
                    TypedResults.Ok(GetRateBudgetMapper.Map(store, timeProvider.GetUtcNow())))
                .WithName("GetRateBudget")
                .WithSummary("Returns current provider rate-limit headroom for all tracked budget keys")
                .Produces<RateBudgetSnapshot>();
        }
    }
}
