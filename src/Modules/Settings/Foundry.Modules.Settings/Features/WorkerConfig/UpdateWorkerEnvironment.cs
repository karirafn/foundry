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

internal static class UpdateWorkerEnvironment
{
    internal sealed record MountRequest(string ContainerPath, string HostPath, bool ReadOnly);

    internal sealed record Command(
        int MemoryLimitMb,
        double CpuLimit,
        int PidsLimit,
        IReadOnlyList<MountRequest> Mounts,
        string HostSettingsPath,
        IReadOnlyList<string> ExcludedHookCommands) : ICommand<GlobalSettingsSummary>;

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

            Result<WorkerResourceLimits> limitsResult = WorkerResourceLimits.Create(
                command.MemoryLimitMb,
                command.CpuLimit,
                command.PidsLimit);

            if (limitsResult is not Result<WorkerResourceLimits>.Success limitsSuccess)
            {
                return Result<GlobalSettingsSummary>.Fail(((Result<WorkerResourceLimits>.Failure)limitsResult).Error);
            }

            Result<WorkerMounts> mountsResult = WorkerMounts.Create(
                [.. command.Mounts.Select(ToMount)]);

            if (mountsResult is not Result<WorkerMounts>.Success mountsSuccess)
            {
                return Result<GlobalSettingsSummary>.Fail(((Result<WorkerMounts>.Failure)mountsResult).Error);
            }

            // System.Text.Json does not enforce non-nullable annotations, so a null exclusion element can arrive.
            Result<HostHooks> hostHooksResult = HostHooks.Create(
                command.HostSettingsPath,
                [.. command.ExcludedHookCommands.Select(excluded => excluded ?? string.Empty)]);

            if (hostHooksResult is not Result<HostHooks>.Success hostHooksSuccess)
            {
                return Result<GlobalSettingsSummary>.Fail(((Result<HostHooks>.Failure)hostHooksResult).Error);
            }

            bool environmentChanged = settings.UpdateWorkerEnvironment(limitsSuccess.Value, mountsSuccess.Value);
            bool hostHooksChanged = settings.UpdateHostHooks(hostHooksSuccess.Value);
            bool changed = environmentChanged || hostHooksChanged;

            if (changed)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return GlobalSettingsMapper.ToSummary(settings);
        }
    }

    // System.Text.Json does not enforce non-nullable annotations, so a null element or path can arrive.
    private static WorkerMount ToMount(MountRequest? request) =>
        request is null
            ? new WorkerMount(string.Empty, string.Empty, ReadOnly: false)
            : new WorkerMount(request.ContainerPath ?? string.Empty, request.HostPath ?? string.Empty, request.ReadOnly);

    internal static class Endpoint
    {
        private sealed record RequestBody(
            int MemoryLimitMb,
            double CpuLimit,
            int PidsLimit,
            IReadOnlyList<MountRequest> Mounts,
            string HostSettingsPath,
            IReadOnlyList<string> ExcludedHookCommands);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPut("/worker-environment", static async (
                    RequestBody body,
                    ICommandHandler<Command, GlobalSettingsSummary> handler,
                    CancellationToken cancellationToken) =>
                {
                    Command command = new(
                        body.MemoryLimitMb,
                        body.CpuLimit,
                        body.PidsLimit,
                        body.Mounts ?? [],
                        body.HostSettingsPath ?? string.Empty,
                        body.ExcludedHookCommands ?? []);

                    Result<GlobalSettingsSummary> result = await handler.HandleAsync(command, cancellationToken);

                    return result.Match<Results<Ok<GlobalSettingsSummary>, ProblemHttpResult>>(
                        summary => TypedResults.Ok(summary),
                        error => error.Code switch
                        {
                            SettingsErrors.NotFoundCode => error.ToProblem(StatusCodes.Status404NotFound),
                            _ => error.ToProblem(StatusCodes.Status400BadRequest),
                        });
                })
                .WithName("UpdateWorkerEnvironment")
                .WithSummary("Updates the worker resource limits, host mounts, and host hooks source")
                .Produces<GlobalSettingsSummary>()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest);
        }
    }
}
