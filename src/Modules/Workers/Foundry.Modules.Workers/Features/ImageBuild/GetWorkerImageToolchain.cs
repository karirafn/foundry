using System.Text.Json.Serialization;

using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Features.Roles;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>One tool the worker image carries.</summary>
internal sealed record WorkerImageTool(string Name, string Version);

/// <summary>
/// The toolchain of the image workers run now: the role catalog's image (ADR 0109). <c>recorded</c> once the image's
/// probe stored a list; <c>unknown</c> until then.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(typeof(RecordedWorkerImageToolchain), typeDiscriminator: "recorded")]
[JsonDerivedType(typeof(UnknownWorkerImageToolchain), typeDiscriminator: "unknown")]
internal abstract record WorkerImageToolchainResponse
{
    private protected WorkerImageToolchainResponse()
    {
    }
}

/// <summary>The current image's toolchain, as its probe stored it.</summary>
/// <param name="ImageId">The image the list belongs to.</param>
/// <param name="RecordedAt">When Foundry probed the image.</param>
/// <param name="Tools">The tools, in the order the image reported them.</param>
internal sealed record RecordedWorkerImageToolchain(string ImageId, DateTimeOffset RecordedAt, IReadOnlyList<WorkerImageTool> Tools)
    : WorkerImageToolchainResponse;

/// <summary>The current image has no stored toolchain yet, or no image is known.</summary>
/// <param name="ImageId">The current image, or <see langword="null"/> when no image is known yet.</param>
internal sealed record UnknownWorkerImageToolchain(string? ImageId) : WorkerImageToolchainResponse;

internal static class GetWorkerImageToolchain
{
    internal static async Task<WorkerImageToolchainResponse> HandleAsync(
        string? imageId,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (imageId is null)
        {
            return new UnknownWorkerImageToolchain(null);
        }

        WorkerImageToolchain? row = await dbContext.Set<WorkerImageToolchain>()
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.ImageId == imageId, cancellationToken);

        if (row is null)
        {
            return new UnknownWorkerImageToolchain(imageId);
        }

        return new RecordedWorkerImageToolchain(
            row.ImageId,
            row.RecordedAt,
            [.. row.Toolchain.Entries.Select(entry => new WorkerImageTool(entry.Name, entry.Version))]);
    }

    internal static class Endpoint
    {
        public static void Map(RouteGroupBuilder group)
        {
            group.MapGet("/", static async (
                    IWorkerRoleCatalogStore store,
                    DbContext dbContext,
                    CancellationToken cancellationToken) =>
                    TypedResults.Ok(await HandleAsync(store.Current?.Image.ImageId, dbContext, cancellationToken)))
                .WithName("GetWorkerImageToolchain")
                .WithSummary("Returns the toolchain recorded for the worker image workers run now, or unknown until it is recorded")
                .Produces<WorkerImageToolchainResponse>();
        }
    }
}
