using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Foundry.Modules.Settings.Features;

internal static class GetLoginCommand
{
    internal sealed record Query : IQuery<OAuthLoginCommand>;

    internal sealed class Handler : IQueryHandler<Query, OAuthLoginCommand>
    {
        public Task<Result<OAuthLoginCommand>> HandleAsync(
            Query query,
            CancellationToken cancellationToken)
        {
            string command = BuildLoginCommand();
            return Task.FromResult(Result<OAuthLoginCommand>.Ok(new OAuthLoginCommand(command)));
        }

        internal static string BuildLoginCommand() =>
            $"docker run -it --rm" +
            $" -v {WorkerVolumeNames.CredentialVolumeName}:{WorkerVolumeNames.ClaudeConfigContainerPath}" +
            $" -e {WorkerVolumeNames.ClaudeConfigDirEnvVar}={WorkerVolumeNames.ClaudeConfigContainerPath}" +
            $" {WorkerImageNames.LoginImageName}" +
            $" claude /login";
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/oauth/login-command", static async (
                    IQueryHandler<Query, OAuthLoginCommand> handler,
                    CancellationToken cancellationToken) =>
                {
                    Result<OAuthLoginCommand> result = await handler.HandleAsync(
                        new Query(),
                        cancellationToken);

                    return result.Match<Ok<OAuthLoginCommand>>(
                        cmd => TypedResults.Ok(cmd),
                        _ => throw new InvalidOperationException("GetLoginCommand cannot fail."));
                })
                .WithName("GetLoginCommand")
                .WithSummary("Gets the ready-to-run docker command to seed the OAuth credential volume")
                .Produces<OAuthLoginCommand>();
        }
    }
}
