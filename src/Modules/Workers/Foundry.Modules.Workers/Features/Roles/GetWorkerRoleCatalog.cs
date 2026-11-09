using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Roles;

/// <summary>The worker image the role catalog belongs to.</summary>
/// <param name="Tag">The worker image tag.</param>
/// <param name="ImageId">The image ID, when known.</param>
/// <param name="BuiltAt">When the image was built (or, with image build disabled, created).</param>
/// <param name="Commit">The Foundry commit the API and the image build context come from, when known.</param>
/// <param name="Verified">
/// Whether the catalog was captured from the image's own build context. <see langword="false"/> when image build is
/// disabled: the catalog was read from disk and may differ from the operator-supplied image.
/// </param>
internal sealed record WorkerRoleImage(string Tag, string? ImageId, DateTimeOffset BuiltAt, string? Commit, bool Verified);

/// <summary>A read-only role card: everything a role contributes to a run.</summary>
/// <param name="Name">The role name, as recorded on worker runs.</param>
/// <param name="Purpose">The role plugin's description.</param>
/// <param name="SystemPrompt">The role's system prompt template, placeholders unfilled.</param>
/// <param name="TaskPrompt">The role's task prompt template, placeholders unfilled.</param>
/// <param name="Skills">The role plugin's skills.</param>
/// <param name="Agents">The role plugin's agents.</param>
/// <param name="VerdictSchema">The JSON schema of the role's verdict; <see langword="null"/> when the role has none.</param>
/// <param name="Model">
/// The role's model setting: a Claude Code alias or a model ID; empty for the CLI's account default. The only editable
/// part of a role (ADR 0095).
/// </param>
internal sealed record WorkerRoleCard(
    string Name,
    string? Purpose,
    string SystemPrompt,
    string TaskPrompt,
    IReadOnlyList<RolePluginComponent> Skills,
    IReadOnlyList<RolePluginComponent> Agents,
    string? VerdictSchema,
    string Model);

/// <summary>The plugin loaded for every role.</summary>
internal sealed record SharedRolePlugin(
    string? Description,
    IReadOnlyList<RolePluginComponent> Skills,
    IReadOnlyList<RolePluginComponent> Agents);

/// <summary>The role catalog of the running worker image, with the system-prompt sections Foundry adds.</summary>
/// <param name="Image">The image the catalog belongs to.</param>
/// <param name="Roles">One card per role.</param>
/// <param name="Shared">The plugin loaded for every role.</param>
/// <param name="PromptSections">the system-prompt sections Foundry adds, in assembly order.</param>
/// <param name="RolePromptSlot">The marker standing for the role's system prompt in <paramref name="PromptSections"/>.</param>
internal sealed record WorkerRoleCatalogResponse(
    WorkerRoleImage Image,
    IReadOnlyList<WorkerRoleCard> Roles,
    SharedRolePlugin Shared,
    IReadOnlyList<PromptSectionDescriptor> PromptSections,
    string RolePromptSlot);

internal static class GetWorkerRoleCatalog
{
    internal static readonly Error NotLoaded = new(
        "Worker.RoleCatalogNotLoaded",
        "The worker role catalog is not loaded yet. It is read during the worker image build; wait for the build to finish.");

    internal static WorkerRoleCatalogResponse ToResponse(
        WorkerRoleCatalog catalog,
        int timeoutMinutes,
        IReadOnlyDictionary<WorkerRole, string> models)
    {
        WorkerImageProvenance image = catalog.Image;

        return new WorkerRoleCatalogResponse(
            new WorkerRoleImage(image.Tag, image.ImageId, image.LoadedAt, image.Commit, image.Verified),
            [
                .. catalog.Content.Roles.Select(role => new WorkerRoleCard(
                    role.Role.Name,
                    role.Plugin.Description,
                    role.SystemPromptTemplate,
                    role.TaskPromptTemplate,
                    role.Plugin.Skills,
                    role.Plugin.Agents,
                    role.VerdictSchema,
                    models.GetValueOrDefault(role.Role, string.Empty))),
            ],
            new SharedRolePlugin(
                catalog.Content.Shared.Description,
                catalog.Content.Shared.Skills,
                catalog.Content.Shared.Agents),
            SystemPromptBuilder.DescribeSections(catalog.Content.Principles, timeoutMinutes),
            SystemPromptBuilder.RolePromptSlot);
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/", static async (
                    IWorkerRoleCatalogStore store,
                    IGlobalSettingsQueries settingsQueries,
                    DbContext dbContext,
                    CancellationToken cancellationToken) =>
                {
                    WorkerRoleCatalog? catalog = store.Current;

                    if (catalog is null)
                    {
                        return (Results<Ok<WorkerRoleCatalogResponse>, ProblemHttpResult>)NotLoaded.ToProblem(
                            StatusCodes.Status503ServiceUnavailable);
                    }

                    int timeoutMinutes = await settingsQueries.GetTimeoutMinutesAsync(cancellationToken);

                    Dictionary<WorkerRole, string> models = await dbContext.Set<WorkerRoleSettings>()
                        .AsNoTracking()
                        .ToDictionaryAsync(settings => settings.Role, settings => settings.Model, cancellationToken);

                    return TypedResults.Ok(ToResponse(catalog, timeoutMinutes, models));
                })
                .WithName("GetWorkerRoleCatalog")
                .WithSummary("Returns the worker roles of the running worker image, read-only, with the system-prompt sections Foundry adds")
                .Produces<WorkerRoleCatalogResponse>()
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        }
    }
}

internal static class WorkerRoleEndpoints
{
    internal static IEndpointRouteBuilder MapWorkerRoleEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/worker-roles")
            .WithTags("Worker Roles");

        GetWorkerRoleCatalog.Endpoint.Map(group);
        GetWorkerRoleModels.Endpoint.Map(group);
        UpdateWorkerRoleModel.Endpoint.Map(group);

        return routes;
    }
}
