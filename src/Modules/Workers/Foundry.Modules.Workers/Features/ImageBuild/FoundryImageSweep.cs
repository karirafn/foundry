using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

using Foundry.Shared.Infrastructure.Docker;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.ImageBuild;

/// <summary>
/// Removes the images Foundry built that no tag points to any more. Every worker Dockerfile labels its images with
/// <see cref="LabelKey"/> right after <c>FROM</c>, so the label also marks each classic-builder step image; images
/// without it belong to other projects on the shared Docker host and are never touched.
/// </summary>
internal sealed class FoundryImageSweep(IImageOperations imageOperations, ILogger<FoundryImageSweep> logger)
{
    internal const string LabelKey = "dev.foundry.image";

    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            int removed = 0;
            int removedInPass;

            // Removing an image can expose its untagged parent step images as dangling, so a pass that removed
            // anything is followed by another until one removes nothing.
            do
            {
                removedInPass = await RemoveDanglingImagesAsync(cancellationToken);
                removed += removedInPass;
            }
            while (removedInPass > 0);

            if (removed > 0)
            {
                logger.LogInformation("Removed {Count} untagged Foundry image(s).", removed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Cleaning up old images is best effort: no Docker failure may fail the build or dispatch that triggered it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning("Removing untagged Foundry images failed: {Error}", SecretRedactor.Redact(ex.Message));
        }
    }

    private async Task<int> RemoveDanglingImagesAsync(CancellationToken cancellationToken)
    {
        IList<ImagesListResponse> images = await imageOperations.ListImagesAsync(
            new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [LabelKey] = true },
                    ["dangling"] = new Dictionary<string, bool> { ["true"] = true },
                },
            },
            cancellationToken);

        int removed = 0;

        foreach (ImagesListResponse image in images)
        {
            if (await TryRemoveAsync(image.ID, cancellationToken))
            {
                removed++;
            }
        }

        return removed;
    }

    private async Task<bool> TryRemoveAsync(string imageId, CancellationToken cancellationToken)
    {
        try
        {
            // Never forced: Docker refuses to remove an image a container still uses, which keeps a running or
            // stopped run's image in place until a later sweep finds its container gone.
            await imageOperations.DeleteImageAsync(imageId, new ImageDeleteParameters(), cancellationToken);
            return true;
        }
        catch (DockerImageNotFoundException)
        {
            logger.LogDebug("Image {ImageId} was already removed.", imageId);
            return false;
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            logger.LogDebug("Image {ImageId} is still in use; skipped: {Error}", imageId, SecretRedactor.Redact(ex.Message));
            return false;
        }
    }
}
