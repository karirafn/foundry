using System.Diagnostics;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.ValueObjects;

namespace Foundry.Modules.Settings.Features;

internal static class ImageBuildStatusMapper
{
    internal static ImageBuildStatus ToStatus(this ImageBuildState state) =>
        state switch
        {
            ImageBuildState.Idle => ImageBuildStatus.Idle,
            ImageBuildState.Building => ImageBuildStatus.Building,
            ImageBuildState.Failed => ImageBuildStatus.Failed,
            _ => throw new UnreachableException($"Unhandled {nameof(ImageBuildState)} variant: {state.GetType().Name}"),
        };
}
