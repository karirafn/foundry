using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features.WorkerConfig;

internal static class UpdatePromptTemplates
{
    internal sealed record Command(string? SystemPromptTemplate, string? WorkerPromptTemplate)
        : ICommand<GlobalSettingsSummary>;

    internal sealed class Validator : ICommandValidator<Command>
    {
        public Result Validate(Command command)
        {
            if (command.SystemPromptTemplate is not null && command.SystemPromptTemplate.Length == 0)
            {
                return SettingsErrors.InvalidPromptTemplate;
            }

            if (command.WorkerPromptTemplate is not null && command.WorkerPromptTemplate.Length == 0)
            {
                return SettingsErrors.InvalidPromptTemplate;
            }

            if (command.SystemPromptTemplate is not null
                && command.SystemPromptTemplate.Length > GlobalSettings.MaxPromptTemplateLength)
            {
                return SettingsErrors.InvalidPromptTemplateTooLong;
            }

            if (command.WorkerPromptTemplate is not null
                && command.WorkerPromptTemplate.Length > GlobalSettings.MaxPromptTemplateLength)
            {
                return SettingsErrors.InvalidPromptTemplateTooLong;
            }

            return Result.Ok();
        }
    }

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

            Result updateResult = settings.UpdatePromptTemplates(command.SystemPromptTemplate, command.WorkerPromptTemplate);

            if (updateResult is Result.Failure updateFailure)
            {
                return Result<GlobalSettingsSummary>.Fail(updateFailure.Error);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return GlobalSettingsMapper.ToSummary(settings);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(string? SystemPromptTemplate, string? WorkerPromptTemplate);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/prompts", static async (
                    RequestBody body,
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.SystemPromptTemplate, body.WorkerPromptTemplate);
                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, NotFound, BadRequest<string>>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => TypedResults.NotFound(),
                            _ => TypedResults.BadRequest(error.Message),
                        });
                })
                .WithName("UpdatePromptTemplates")
                .WithSummary("Updates the system and worker prompt templates")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
