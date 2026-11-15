using System.Net;

using Docker.DotNet;
using Docker.DotNet.Models;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.FoundryImageSweepTests;

/// <summary>
/// An in-memory Docker image store that answers <see cref="IImageOperations.ListImagesAsync"/> and
/// <see cref="IImageOperations.DeleteImageAsync"/> the way the daemon does: the <c>label</c> and <c>dangling</c>
/// filters are applied only when the caller passes them, a top-level listing hides images that have children, and a
/// removal refuses an image a container uses unless forced. A removal takes only the image itself, as the daemon does
/// when another image still holds the parent at that moment, so its parent surfaces as dangling for the next listing.
/// </summary>
internal sealed class FakeImageHost : IImageOperations
{
    private readonly List<FakeImage> _images = [];

    public List<ImagesListParameters> ListCalls { get; } = [];

    public List<(string Name, ImageDeleteParameters Parameters)> DeleteCalls { get; } = [];

    public Exception? ListFailure { get; set; }

    public Exception? DeleteFailure { get; set; }

    public IEnumerable<string> RemainingIds => _images.Select(i => i.Id);

    public FakeImageHost Add(
        string id,
        string? label = "worker",
        string? tag = null,
        string? parent = null,
        bool inUse = false)
    {
        _images.Add(new FakeImage(id, label, tag, parent, inUse));
        return this;
    }

    public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken)
    {
        ListCalls.Add(parameters);

        if (ListFailure is not null)
        {
            throw ListFailure;
        }

        IDictionary<string, IDictionary<string, bool>> filters =
            parameters.Filters ?? new Dictionary<string, IDictionary<string, bool>>();

        IList<ImagesListResponse> listed = _images
            .Where(image => !HasChildren(image))
            .Where(image => !filters.TryGetValue("label", out IDictionary<string, bool>? labels)
                || labels.Keys.All(key => key == FakeImage.LabelKey && image.Label is not null))
            .Where(image => !filters.TryGetValue("dangling", out IDictionary<string, bool>? dangling)
                || dangling.ContainsKey("true") == (image.Tag is null))
            .Select(image => new ImagesListResponse
            {
                ID = image.Id,
                RepoTags = image.Tag is null ? [] : [image.Tag],
                Labels = image.Label is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { [FakeImage.LabelKey] = image.Label },
            })
            .ToList();

        return Task.FromResult(listed);
    }

    public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken)
    {
        DeleteCalls.Add((name, parameters));

        if (DeleteFailure is not null)
        {
            throw DeleteFailure;
        }

        FakeImage? image = _images.Find(i => i.Id == name);

        if (image is null)
        {
            throw new DockerImageNotFoundException(HttpStatusCode.NotFound, $"No such image: {name}");
        }

        if (image.InUse && parameters.Force != true)
        {
            // Status and body captured from `DELETE /images/{id}` on a Docker daemon for the image of a
            // stopped container (2026-11-15).
            throw new DockerApiException(
                HttpStatusCode.Conflict,
                "{\"message\":\"conflict: unable to delete " + name + " (must be forced) - image is being used by stopped container 27c2a8fef929\"}");
        }

        _images.Remove(image);
        IList<IDictionary<string, string>> response = [new Dictionary<string, string> { ["Deleted"] = name }];
        return Task.FromResult(response);
    }

    private bool HasChildren(FakeImage image) => _images.Exists(other => other.Parent == image.Id);

    public Task BuildImageFromDockerfileAsync(ImageBuildParameters parameters, Stream contents, IEnumerable<AuthConfig> authConfigs, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
#pragma warning disable CS0618 // Required for interface compliance
    public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken) => throw new NotSupportedException();
#pragma warning restore CS0618
    public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IList<ImageSearchResponse>> SearchImagesAsync(ImagesSearchParameters parameters, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task CreateImageAsync(ImagesCreateParameters parameters, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task CreateImageAsync(ImagesCreateParameters parameters, Stream imageStream, AuthConfig authConfig, IDictionary<string, string> headers, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task LoadImageAsync(ImageLoadParameters parameters, Stream imageStream, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Stream> SaveImageAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Stream> SaveImagesAsync(string[] names, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task TagImageAsync(string name, ImageTagParameters parameters, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task PushImageAsync(string name, ImagePushParameters parameters, AuthConfig authConfig, IProgress<JSONMessage> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ImagesPruneResponse> PruneImagesAsync(ImagesPruneParameters parameters, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CommitContainerChangesResponse> CommitContainerChangesAsync(CommitContainerChangesParameters parameters, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IList<ImageHistoryResponse>> GetImageHistoryAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

    private sealed record FakeImage(string Id, string? Label, string? Tag, string? Parent, bool InUse)
    {
        public const string LabelKey = "dev.foundry.image";
    }
}
