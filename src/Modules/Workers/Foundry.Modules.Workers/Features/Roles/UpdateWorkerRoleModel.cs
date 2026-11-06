using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Roles.Models;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Roles;

/// <summary>A role's saved model setting.</summary>
/// <param name="Role">The role name.</param>
/// <param name="Model">The saved model; empty for the CLI's account default.</param>
internal sealed record WorkerRoleModelResponse(string Role, string Model);

internal static class UpdateWorkerRoleModel
{
    internal const string RoleNotFoundCode = "Worker.RoleNotFound";
    internal const string ModelNotListedCode = "Worker.RoleModelNotListed";
    internal const string ModelTooLongCode = "Worker.RoleModelTooLong";

    internal static Error RoleNotFound(string role) =>
        new(RoleNotFoundCode, $"There is no worker role named '{role}'.") { Kind = ErrorKind.NotFound };

    internal static readonly Error ModelTooLong =
        new(ModelTooLongCode, $"The model must not exceed {WorkerRoleSettings.MaxModelLength} characters.");

    internal static Error ModelNotListed(string model, string? refreshError) =>
        new(
            ModelNotListedCode,
            refreshError is null
                ? $"'{model}' is not a Claude Code alias or a model listed by the Models API. Pick one from the list."
                : $"'{model}' is not a Claude Code alias, and the model list could not be refreshed: {refreshError}");

    internal sealed record Command(string Role, string Model) : ICommand<WorkerRoleModelResponse>;

    internal sealed class Handler(DbContext dbContext, IClaudeModelCatalog modelCatalog)
        : ICommandHandler<Command, WorkerRoleModelResponse>
    {
        public async Task<Result<WorkerRoleModelResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            WorkerRole? role = WorkerRole.All.FirstOrDefault(
                candidate => string.Equals(candidate.Name, command.Role, StringComparison.Ordinal));

            if (role is null)
            {
                return Result<WorkerRoleModelResponse>.Fail(RoleNotFound(command.Role));
            }

            string model = command.Model.Trim();

            if (model.Length > WorkerRoleSettings.MaxModelLength)
            {
                return Result<WorkerRoleModelResponse>.Fail(ModelTooLong);
            }

            if (model.Length > 0 && !ClaudeModelAliases.IsAlias(model))
            {
                // Validated against the current cached list; it is fetched first when it never was.
                ClaudeModelList list = await modelCatalog.GetAsync(cancellationToken);

                if (!list.Lists(model))
                {
                    return Result<WorkerRoleModelResponse>.Fail(ModelNotListed(model, list.RefreshError));
                }
            }

            WorkerRoleSettings? settings = await dbContext.Set<WorkerRoleSettings>().FindAsync([role], cancellationToken);

            if (settings is null)
            {
                settings = WorkerRoleSettings.Create(role);
                dbContext.Set<WorkerRoleSettings>().Add(settings);
            }

            settings.ChangeModel(model);

            await dbContext.SaveChangesAsync(cancellationToken);

            return new WorkerRoleModelResponse(role.Name, settings.Model);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(string? Model);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/{role}/model", static async (
                    string role,
                    RequestBody body,
                    ICommandHandler<Command, WorkerRoleModelResponse> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<WorkerRoleModelResponse> result = await handler.HandleAsync(
                        new Command(role, body.Model ?? string.Empty),
                        cancellationToken);

                    return result.Match<Results<Ok<WorkerRoleModelResponse>, ProblemHttpResult>>(
                        response => TypedResults.Ok(response),
                        error => error.Code switch
                        {
                            RoleNotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            _ => error.ToProblem(StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("UpdateWorkerRoleModel")
                .WithSummary("Sets a worker role's model: empty for the CLI's account default, a Claude Code alias, or a model ID listed by the Models API")
                .Produces<WorkerRoleModelResponse>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
