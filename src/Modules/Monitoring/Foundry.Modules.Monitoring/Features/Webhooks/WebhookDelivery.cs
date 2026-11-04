using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Provider-neutral steps of receiving a webhook delivery (ADR 0085): hook id lookup, bounded body read,
/// parsing and loading the relevance-filter context. Verification itself is provider-specific.
/// </summary>
internal static class WebhookDelivery
{
    /// <summary>Upper bound on a delivery body; larger bodies are rejected with 413 before verification.</summary>
    internal const int MaxBodyBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Loads the repository addressed by <paramref name="hookId"/>, or <see langword="null"/> when the id is
    /// malformed or unknown (both answered 404, without touching the body).
    /// </summary>
    internal static async Task<MonitoredRepository?> FindRepositoryAsync(
        DbContext dbContext,
        string hookId,
        CancellationToken cancellationToken)
    {
        if (!IsWellFormedHookId(hookId))
        {
            return null;
        }

        return await dbContext.Set<MonitoredRepository>()
            .FirstOrDefaultAsync(r => r.WebhookHookId == hookId, cancellationToken);
    }

    /// <summary>Reads the raw body, or returns <see langword="null"/> when it exceeds <see cref="MaxBodyBytes"/>.</summary>
    internal static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is > MaxBodyBytes)
        {
            return null;
        }

        using MemoryStream buffer = new();
        byte[] chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    internal static JsonDocument? TryParse(byte[] body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="root"/>.<paramref name="objectProperty"/>.<paramref name="pathProperty"/>
    /// names <paramref name="repository"/> (case-insensitive; nested GitLab group paths included).
    /// </summary>
    internal static bool NamesRepository(
        JsonElement root,
        string objectProperty,
        string pathProperty,
        MonitoredRepository repository) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(objectProperty, out JsonElement owner)
        && owner.ValueKind == JsonValueKind.Object
        && owner.TryGetProperty(pathProperty, out JsonElement path)
        && path.ValueKind == JsonValueKind.String
        && string.Equals(path.GetString(), repository.Slug.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Loads what Foundry tracks for the repository. Blockers are loaded only when
    /// <paramref name="includeBlockers"/> is set (GitLab issue events), since they cost an extra query.
    /// </summary>
    internal static async Task<WebhookTrackingContext> LoadTrackingContextAsync(
        IIssueQueries issueQueries,
        MonitoredRepositoryId repositoryId,
        bool includeBlockers,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<int> known = await issueQueries.GetKnownIssueNumbersAsync(repositoryId, cancellationToken);
        IReadOnlyList<ReviewIssueInfo> review = await issueQueries.GetReviewIssuesAsync(repositoryId, cancellationToken);
        IReadOnlyList<ChecksPendingIssueInfo> checksPending = await issueQueries.GetChecksPendingIssuesAsync(
            repositoryId,
            cancellationToken);

        IReadOnlySet<int>? blockers = null;
        if (includeBlockers)
        {
            IReadOnlyList<DependencyEdge> edges = await issueQueries.GetDependencyGraphAsync(repositoryId, cancellationToken);
            blockers = edges.Select(e => e.BlockedByIssueNumber).ToHashSet();
        }

        return new WebhookTrackingContext(known, review, checksPending, blockers);
    }

    private static bool IsWellFormedHookId(string hookId) =>
        hookId.Length == MonitoredRepository.WebhookHookIdLength
        && hookId.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'));
}
