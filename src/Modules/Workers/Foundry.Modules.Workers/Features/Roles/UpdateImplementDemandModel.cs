using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Features.Roles.Models;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.Roles;

/// <summary>The Tier Model of one Implement Demand.</summary>
/// <param name="Demand">The Implement Demand the setting applies to.</param>
/// <param name="Model">The model; empty for the CLI's account default.</param>
/// <param name="Effort">The effort level; empty leaves the model's default.</param>
internal sealed record ImplementDemandModel(ImplementDemand Demand, string Model, string Effort);

internal static class UpdateImplementDemandModel
{
    internal const string DemandNotFoundCode = "Worker.TierDemandNotFound";
    internal const string EffortInvalidCode = "Worker.TierEffortInvalid";

    internal static Error DemandNotFound(string demand) =>
        new(DemandNotFoundCode, $"There is no implement demand named '{demand}'.") { Kind = ErrorKind.NotFound };

    internal static Error EffortInvalid(string effort) =>
        new(
            EffortInvalidCode,
            $"'{effort}' is not an effort level. Use one of: {string.Join(", ", ImplementTierSettings.EffortLevels)}, or leave it empty for the model's default.");

    internal sealed record Command(string Demand, string Model, string Effort) : ICommand<ImplementDemandModel>;

    internal sealed class Handler(DbContext dbContext, IClaudeModelCatalog modelCatalog)
        : ICommandHandler<Command, ImplementDemandModel>
    {
        public async Task<Result<ImplementDemandModel>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ImplementDemand? demand = ParseDemand(command.Demand);

            if (demand is not { } parsed)
            {
                return Result<ImplementDemandModel>.Fail(DemandNotFound(command.Demand));
            }

            string effort = command.Effort.Trim();

            if (!ImplementTierSettings.IsValidEffort(effort))
            {
                return Result<ImplementDemandModel>.Fail(EffortInvalid(effort));
            }

            Result<string> validated = await RoleModelValidation.ValidateAsync(command.Model, modelCatalog, cancellationToken);

            if (validated is Result<string>.Failure failure)
            {
                return Result<ImplementDemandModel>.Fail(failure.Error);
            }

            string model = ((Result<string>.Success)validated).Value;

            ImplementTierSettings? settings =
                await dbContext.Set<ImplementTierSettings>().FindAsync([parsed], cancellationToken);

            if (settings is null)
            {
                settings = ImplementTierSettings.Create(parsed);
                dbContext.Set<ImplementTierSettings>().Add(settings);
            }

            settings.ChangeModel(model);
            settings.ChangeEffort(effort);

            await dbContext.SaveChangesAsync(cancellationToken);

            return new ImplementDemandModel(settings.Demand, settings.Model, settings.Effort);
        }

        private static ImplementDemand? ParseDemand(string demand) =>
            demand switch
            {
                "routine" => ImplementDemand.Routine,
                "demanding" => ImplementDemand.Demanding,
                _ => null,
            };
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(string? Model, string? Effort);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/implement/demand-models/{demand}", static async (
                    string demand,
                    RequestBody body,
                    ICommandHandler<Command, ImplementDemandModel> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<ImplementDemandModel> result = await handler.HandleAsync(
                        new Command(demand, body.Model ?? string.Empty, body.Effort ?? string.Empty),
                        cancellationToken);

                    return result.Match<Results<Ok<ImplementDemandModel>, ProblemHttpResult>>(
                        response => TypedResults.Ok(response),
                        error => error.Code switch
                        {
                            DemandNotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            _ => error.ToProblem(StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("UpdateImplementDemandModel")
                .WithSummary("Sets the implement role's model and effort for one Implement Demand (routine or demanding): an empty model for the CLI's account default, a Claude Code alias, or a model ID listed by the Models API; an empty effort for the model's default")
                .Produces<ImplementDemandModel>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
