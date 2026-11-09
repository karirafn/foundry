using Docker.DotNet;
using Docker.DotNet.Models;

namespace Foundry.IntegrationTests.Smoke;

/// <summary>
/// Docker image operations whose first build is held open until the test releases it, standing in for
/// a slow startup image build. Every build then succeeds and every inspect finds the image.
/// </summary>
internal sealed class HeldImageBuild : IImageOperations
{
    internal const string ImageId = "sha256:smoke-test-worker-image";

    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the first build reaches the daemon.</summary>
    public Task Started => _started.Task;

    public void Release() => _released.TrySetResult();

    public async Task BuildImageFromDockerfileAsync(
        ImageBuildParameters parameters,
        Stream contents,
        IEnumerable<AuthConfig> authConfigs,
        IDictionary<string, string> headers,
        IProgress<JSONMessage> progress,
        CancellationToken cancellationToken)
    {
        _started.TrySetResult();
        await _released.Task.WaitAsync(cancellationToken);
    }

    public Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(new ImageInspectResponse { ID = ImageId, Created = DateTime.UtcNow });

#pragma warning disable CS0618 // Required for interface compliance
    public Task<Stream> BuildImageFromDockerfileAsync(Stream contents, ImageBuildParameters parameters, CancellationToken cancellationToken)
        => throw new NotSupportedException();
#pragma warning restore CS0618

    public Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters parameters, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters parameters, CancellationToken cancellationToken) => throw new NotSupportedException();
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
}
