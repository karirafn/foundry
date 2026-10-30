using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features.WorkerConfig;

internal static class UpdatePromptTemplates
{
    internal sealed record Command(
        string SystemPromptTemplate,
        string WorkerPromptTemplate,
        string BranchNamingInstruction)
        : ICommand<GlobalSettingsSummary>;

    internal sealed class Handler(DbContext dbContext) : ICommandHandler<Command, GlobalSettingsSummary>
    {
        public async Task<Result<GlobalSettingsSummary>> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
                .FirstOrDefaultAsync(cancellationToken);

            if (settings is null)
            {
                return Result<GlobalSettingsSummary>.Fail(SettingsErrors.NotFound);
            }

            Result<WorkerPrompts> promptsResult = WorkerPrompts.Create(
                command.SystemPromptTemplate,
                command.WorkerPromptTemplate,
                command.BranchNamingInstruction);

            if (promptsResult is not Result<WorkerPrompts>.Success promptsSuccess)
            {
                return Result<GlobalSettingsSummary>.Fail(((Result<WorkerPrompts>.Failure)promptsResult).Error);
            }

            settings.UpdatePrompts(promptsSuccess.Value);

            await dbContext.SaveChangesAsync(cancellationToken);

            return GlobalSettingsMapper.ToSummary(settings);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(
            string SystemPromptTemplate,
            string WorkerPromptTemplate,
            string BranchNamingInstruction);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/prompts", static async (
                    RequestBody body,
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.SystemPromptTemplate, body.WorkerPromptTemplate, body.BranchNamingInstruction);
                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, ProblemHttpResult>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            _ => error.ToProblem(StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("UpdatePromptTemplates")
                .WithSummary("Updates the system prompt, worker prompt, and branch naming instruction")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
