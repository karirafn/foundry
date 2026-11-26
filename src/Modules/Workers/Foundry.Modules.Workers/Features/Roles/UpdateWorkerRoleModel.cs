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
    internal const string RoleModelPerDemandCode = "Worker.RoleModelPerDemand";

    internal static Error RoleNotFound(string role) =>
        new(RoleNotFoundCode, $"There is no worker role named '{role}'.") { Kind = ErrorKind.NotFound };

    internal static readonly Error RoleModelPerDemand = new(
        RoleModelPerDemandCode,
        "The implement role's model is set per demand: use PUT /api/worker-roles/implement/demand-models/{demand}.");

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

            if (role == WorkerRole.Implement)
            {
                return Result<WorkerRoleModelResponse>.Fail(RoleModelPerDemand);
            }

            Result<string> validated = await RoleModelValidation.ValidateAsync(command.Model, modelCatalog, cancellationToken);

            if (validated is Result<string>.Failure failure)
            {
                return Result<WorkerRoleModelResponse>.Fail(failure.Error);
            }

            string model = ((Result<string>.Success)validated).Value;

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
                .WithSummary("Sets the triage or review role's model: empty for the CLI's account default, a Claude Code alias, or a model ID listed by the Models API; the implement role's model is set per demand")
                .Produces<WorkerRoleModelResponse>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
