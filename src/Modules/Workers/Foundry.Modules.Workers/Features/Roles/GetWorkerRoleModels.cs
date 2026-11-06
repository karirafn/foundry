using Foundry.Modules.Workers.Features.Roles.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Workers.Features.Roles;

/// <summary>A model a role can be set to, as listed by the Anthropic Models API.</summary>
/// <param name="Id">The model ID.</param>
/// <param name="DisplayName">The human-readable name.</param>
/// <param name="Deprecated">Whether the API reports the model as deprecated.</param>
internal sealed record WorkerRoleModelOption(string Id, string DisplayName, bool Deprecated);

/// <summary>The values a role's model can be set to, besides empty (the CLI's account default).</summary>
/// <param name="Aliases">The documented Claude Code aliases.</param>
/// <param name="Models">The model IDs from the last successful Models API call.</param>
/// <param name="RefreshedAt">When <paramref name="Models"/> was fetched; <see langword="null"/> if it never was.</param>
/// <param name="RefreshError">Why the list could not be refreshed; <see langword="null"/> when the last refresh succeeded.</param>
internal sealed record WorkerRoleModelOptionsResponse(
    IReadOnlyList<string> Aliases,
    IReadOnlyList<WorkerRoleModelOption> Models,
    DateTimeOffset? RefreshedAt,
    string? RefreshError);

internal static class GetWorkerRoleModels
{
    internal static WorkerRoleModelOptionsResponse ToResponse(ClaudeModelList list) =>
        new(
            ClaudeModelAliases.All,
            [.. list.Models.Select(model => new WorkerRoleModelOption(model.Id, model.DisplayName, model.Deprecated))],
            list.RefreshedAt,
            list.RefreshError);

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/models", static async (
                    bool? refresh,
                    IClaudeModelCatalog modelCatalog,
                    CancellationToken cancellationToken) =>
                {
                    ClaudeModelList list = refresh == true
                        ? await modelCatalog.RefreshAsync(cancellationToken)
                        : await modelCatalog.GetAsync(cancellationToken);

                    return TypedResults.Ok(ToResponse(list));
                })
                .WithName("GetWorkerRoleModels")
                .WithSummary("Returns the Claude Code aliases and the models the workers' credential can use; refresh=true fetches the list from the Models API now")
                .Produces<WorkerRoleModelOptionsResponse>();
        }
    }
}
