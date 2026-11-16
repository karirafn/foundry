using System.Text.Json.Serialization;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

/// <summary>
/// Which worker image runs use and whether Foundry builds it. Every image carries the full toolchain
/// (ADR 0109), so the configuration holds no install flags; stored rows that still carry them deserialize
/// with those members skipped.
/// </summary>
public sealed record WorkerImageConfiguration
{
    private const string LatestTag = ":latest";
    private const string DefaultImageTag = "foundry-worker:local";

    // Docker image references are capped at 255 characters by the Docker specification.
    internal const int MaxImageTagLength = 255;

    [JsonConstructor]
    private WorkerImageConfiguration(string imageTag, bool buildEnabled)
    {
        ImageTag = imageTag;
        BuildEnabled = buildEnabled;
    }

    public static readonly WorkerImageConfiguration Default = Create(
        imageTag: DefaultImageTag,
        buildEnabled: true)
        .Match(
            onSuccess: v => v,
            onFailure: e => throw new InvalidOperationException($"WorkerImageConfiguration.Default is invalid: {e.Message}"));

    public string ImageTag { get; }

    public bool BuildEnabled { get; }

    public static Result<WorkerImageConfiguration> Create(string imageTag, bool buildEnabled)
    {
        if (string.IsNullOrWhiteSpace(imageTag))
        {
            return Result<WorkerImageConfiguration>.Fail(SettingsErrors.EmptyImageTag);
        }

        if (imageTag.Length > MaxImageTagLength)
        {
            return Result<WorkerImageConfiguration>.Fail(SettingsErrors.ImageTagTooLong);
        }

        if (imageTag.EndsWith(LatestTag, StringComparison.OrdinalIgnoreCase))
        {
            return Result<WorkerImageConfiguration>.Fail(SettingsErrors.LatestImageTag);
        }

        return Result<WorkerImageConfiguration>.Ok(new WorkerImageConfiguration(imageTag, buildEnabled));
    }
}
