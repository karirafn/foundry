using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

using Docker.DotNet;
using Docker.DotNet.Models;

namespace Foundry.Shared.Infrastructure.Docker;

internal sealed class DockerContainerRuntime(
    IContainerOperations containerOperations,
    IVolumeOperations volumeOperations,
    IExecOperations execOperations) : IDockerContainerRuntime
{
    public async Task<string> CreateAndStartAsync(
        CreateContainerParameters createParams,
        CancellationToken cancellationToken)
    {
        CreateContainerResponse response = await containerOperations.CreateContainerAsync(
            createParams,
            cancellationToken);

        await containerOperations.StartContainerAsync(
            response.ID,
            new ContainerStartParameters(),
            cancellationToken);

        return response.ID;
    }

    public async Task StopAsync(
        string containerId,
        int waitBeforeKillSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            await containerOperations.StopContainerAsync(
                containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = (uint)waitBeforeKillSeconds },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Container already gone — treat as successful stop.
        }
    }

    public async Task RemoveAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await containerOperations.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Container already gone — treat as successful removal.
        }
        catch (DockerApiException ex)
            when (ex.StatusCode == HttpStatusCode.Conflict
                  && ex.ResponseBody.Contains("already in progress", StringComparison.OrdinalIgnoreCase))
        {
            // AutoRemove raced our explicit call — container is being removed, which is the desired end state.
        }
    }

    public async Task<ContainerInspectResponse> InspectAsync(
        string containerId,
        CancellationToken cancellationToken)
    {
        return await containerOperations.InspectContainerAsync(
            containerId,
            cancellationToken);
    }

    public async Task<IList<ContainerListResponse>> ListAsync(
        ContainersListParameters parameters,
        CancellationToken cancellationToken)
    {
        return await containerOperations.ListContainersAsync(
            parameters,
            cancellationToken);
    }

    public async IAsyncEnumerable<string> StreamLogsAsync(
        string containerId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ContainerLogsParameters logsParams = new()
        {
            Follow = true,
            ShowStdout = true,
            ShowStderr = true,
            Timestamps = true,
        };

        using MultiplexedStream multiplexedStream = await containerOperations.GetContainerLogsAsync(
            containerId,
            false,
            logsParams,
            cancellationToken);

        Pipe pipe = new();

        using CancellationTokenSource pumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task copyTask = Task.Run(
            async () =>
            {
                Exception? pumpFault = null;
                try
                {
                    await multiplexedStream.CopyOutputToAsync(
                        Stream.Null,
                        pipe.Writer.AsStream(),
                        pipe.Writer.AsStream(),
                        pumpCts.Token);
                }
                catch (Exception ex) when (
                    ex is EndOfStreamException
                    || (ex is IOException ioEx
                        && ioEx.Message.Contains("Unexpected end of stream", StringComparison.Ordinal)))
                {
                    // The Docker log stream terminates when the container exits.
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Unexpected pump fault — record it so CompleteAsync forwards it to the pipe reader.
                    pumpFault = ex;
                }
                finally
                {
                    await pipe.Writer.CompleteAsync(pumpFault);
                }
            },
            pumpCts.Token);

        using StreamReader reader = new(pipe.Reader.AsStream());

        try
        {
            string? line;
            while ((line = await ReadLineOrEndAsync(reader, cancellationToken)) is not null)
            {
                yield return line;
            }
        }
        finally
        {
#pragma warning disable CA1031 // Intentional catch-all in teardown: copyTask is a background pump being shut down; any exception here is irrelevant to the consumer
            await pumpCts.CancelAsync();
            try
            {
                await copyTask;
            }
            catch (OperationCanceledException)
            {
                // Expected: pumpCts was just cancelled above, so an OCE from the pump is a normal teardown signal.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Swallow — residual pump exception during teardown; irrelevant to the consumer.
            }
#pragma warning restore CA1031
        }
    }

    public async Task<string?> GetLogsAsync(
        string containerId,
        int tailLines,
        CancellationToken cancellationToken)
    {
        ContainerLogsParameters logsParams = new()
        {
            Follow = false,
            ShowStdout = true,
            ShowStderr = true,
            Timestamps = true,
            Tail = tailLines.ToString(CultureInfo.InvariantCulture),
        };

        try
        {
            using MultiplexedStream multiplexedStream = await containerOperations.GetContainerLogsAsync(
                containerId,
                false,
                logsParams,
                cancellationToken);

            using MemoryStream outputStream = new();
            await multiplexedStream.CopyOutputToAsync(
                Stream.Null,
                outputStream,
                outputStream,
                cancellationToken);

            outputStream.Seek(0, SeekOrigin.Begin);
            using StreamReader reader = new(outputStream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Container already gone — no logs to return.
            return null;
        }
    }

    public async Task CreateVolumeAsync(
        VolumesCreateParameters parameters,
        CancellationToken cancellationToken)
    {
        await volumeOperations.CreateAsync(
            parameters,
            cancellationToken);
    }

    public async Task<string> ExecCaptureStdoutAsync(
        string containerId,
        IReadOnlyList<string> command,
        IReadOnlyList<string> environment,
        int? maxBytes,
        CancellationToken cancellationToken)
    {
        ContainerExecCreateParameters execParams = new()
        {
            Cmd = [.. command],
            AttachStdin = false,
            AttachStdout = true,
            AttachStderr = false,
        };

        if (environment.Count > 0)
        {
            execParams.Env = [.. environment];
        }

        ContainerExecCreateResponse execResponse = await execOperations.ExecCreateContainerAsync(
            containerId,
            execParams,
            cancellationToken);

        using MultiplexedStream stream = await execOperations.StartAndAttachContainerExecAsync(
            execResponse.ID,
            false,
            cancellationToken);

        using MemoryStream stdoutStream = new();
        await stream.CopyOutputToAsync(Stream.Null, stdoutStream, Stream.Null, cancellationToken);

        stdoutStream.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(stdoutStream, Encoding.UTF8);

        if (maxBytes is null)
        {
            return await reader.ReadToEndAsync(cancellationToken);
        }

        char[] buffer = new char[maxBytes.Value];
        int charsRead = await reader.ReadAsync(buffer, cancellationToken);
        return new string(buffer, 0, charsRead);
    }

    public async Task<string?> ReadFileAsync(
        string containerId,
        string filePath,
        CancellationToken cancellationToken)
    {
        string content = await ExecCaptureStdoutAsync(
            containerId,
            ["cat", filePath],
            [],
            maxBytes: null,
            cancellationToken);

        return string.IsNullOrWhiteSpace(content) ? null : content;
    }

    public async Task WriteFileAsync(
        string containerId,
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        // Pass content via env J and path via env P — neither is interpolated — to avoid shell injection.
        ContainerExecCreateResponse execResponse = await execOperations.ExecCreateContainerAsync(
            containerId,
            new ContainerExecCreateParameters
            {
                Cmd = ["sh", "-c", "printf '%s' \"$J\" > \"$P\""],
                Env = [$"J={content}", $"P={filePath}"],
                AttachStdout = false,
                AttachStderr = false,
            },
            cancellationToken);

        using MultiplexedStream stream = await execOperations.StartAndAttachContainerExecAsync(
            execResponse.ID,
            false,
            cancellationToken);

        await stream.CopyOutputToAsync(Stream.Null, Stream.Null, Stream.Null, cancellationToken);
    }

    public async Task ExecAsync(
        string containerId,
        IReadOnlyList<string> command,
        IReadOnlyList<string> environment,
        CancellationToken cancellationToken)
    {
        ContainerExecCreateParameters execParams = new()
        {
            Cmd = [.. command],
            Env = [.. environment],
            AttachStdin = false,
            AttachStdout = true,
            AttachStderr = true,
        };

        ContainerExecCreateResponse execResponse = await execOperations.ExecCreateContainerAsync(
            containerId,
            execParams,
            cancellationToken);

        using MultiplexedStream stream = await execOperations.StartAndAttachContainerExecAsync(
            execResponse.ID,
            false,
            cancellationToken);

        await stream.CopyOutputToAsync(Stream.Null, Stream.Null, Stream.Null, cancellationToken);
    }

    /// <summary>
    /// Reads the next line from <paramref name="reader"/>, passing <paramref name="cancellationToken"/>
    /// for prompt cancellation. When the consumer cancels (OperationCanceledException), returns
    /// <see langword="null"/> so the read loop can exit as a clean <c>yield break</c> rather than
    /// letting the OCE escape the async iterator.
    /// </summary>
    /// <remarks>
    /// The catch lives here because <c>yield return</c> cannot appear inside a <c>try/catch</c> block.
    /// </remarks>
    private static async Task<string?> ReadLineOrEndAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadLineAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Consumer cancelled — signal end-of-stream rather than propagating the OCE.
            return null;
        }
    }
}
