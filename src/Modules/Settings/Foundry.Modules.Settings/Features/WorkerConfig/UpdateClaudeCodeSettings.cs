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

internal static class UpdateClaudeCodeSettings
{
    internal sealed record Command(IReadOnlyList<string> AdditionalDenyRules) : ICommand<GlobalSettingsSummary>;

    internal sealed class Handler(DbContext dbContext)
        : ICommandHandler<Command, GlobalSettingsSummary>
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

            // System.Text.Json does not enforce non-nullable annotations, so a null rule element can arrive.
            Result<ClaudeCodeSettings> claudeCodeResult = ClaudeCodeSettings.Create(
                [.. command.AdditionalDenyRules.Select(rule => rule ?? string.Empty)]);

            if (claudeCodeResult is not Result<ClaudeCodeSettings>.Success claudeCodeSuccess)
            {
                return Result<GlobalSettingsSummary>.Fail(((Result<ClaudeCodeSettings>.Failure)claudeCodeResult).Error);
            }

            bool changed = settings.UpdateClaudeCodeSettings(claudeCodeSuccess.Value);

            if (changed)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return GlobalSettingsMapper.ToSummary(settings);
        }
    }

    internal static class Endpoint
    {
        private sealed record RequestBody(IReadOnlyList<string> AdditionalDenyRules);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/claude-code", static async (
                    RequestBody body,
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(body.AdditionalDenyRules ?? []);

                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, ProblemHttpResult>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            _ => error.ToProblem(StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("UpdateClaudeCodeSettings")
                .WithSummary("Updates the Claude Code deny rules")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
