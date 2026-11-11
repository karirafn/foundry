using System.Globalization;
using System.Text;

using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Entities;

/// <summary>
/// The Run Transcript: the Agent Transcript of a worker run's full container log, rendered when the
/// run completes, before its container is removed. Stored one row per run, apart from the
/// <see cref="WorkerRun"/> so loading a run never loads its transcript.
/// </summary>
public sealed class WorkerRunTranscript : Entity<WorkerRunId>
{
    /// <summary>The largest transcript stored, in UTF-8 bytes; a longer one keeps its head and tail.</summary>
    public const int MaxTranscriptBytes = 256 * 1024;

    // Room left for the omission marker and its line breaks within the cap.
    private const int MarkerReserveBytes = 128;

    // Private parameterless constructor for EF Core materialization.
    private WorkerRunTranscript() : base(WorkerRunId.New())
    {
    }

    private WorkerRunTranscript(WorkerRunId workerRunId, string transcript, DateTimeOffset capturedAt)
        : base(workerRunId)
    {
        Transcript = transcript;
        CapturedAt = capturedAt;
    }

    public string Transcript { get; private set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; private set; }

    /// <summary>
    /// Captures <paramref name="renderedTranscript"/> for <paramref name="workerRunId"/>, cut to
    /// <see cref="MaxTranscriptBytes"/> when longer.
    /// </summary>
    public static WorkerRunTranscript Capture(
        WorkerRunId workerRunId,
        string renderedTranscript,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(renderedTranscript);

        return new WorkerRunTranscript(workerRunId, Cap(renderedTranscript), capturedAt);
    }

    // Keeps the head (how the run started) and the tail (how it ended), cut on line boundaries, with
    // a marker line stating how much of the middle was omitted.
    private static string Cap(string transcript)
    {
        if (Encoding.UTF8.GetByteCount(transcript) <= MaxTranscriptBytes)
        {
            return transcript;
        }

        string[] lines = transcript.Split('\n');
        int[] lineBytes = [.. lines.Select(line => Encoding.UTF8.GetByteCount(line) + 1)];
        int keptBudget = MaxTranscriptBytes - MarkerReserveBytes;

        int headCount = 0;
        int headBytes = 0;

        while (headCount < lines.Length && headBytes + lineBytes[headCount] <= keptBudget / 2)
        {
            headBytes += lineBytes[headCount];
            headCount++;
        }

        int tailStart = lines.Length;
        int tailBytes = 0;

        while (tailStart > headCount && tailBytes + lineBytes[tailStart - 1] <= keptBudget - headBytes)
        {
            tailBytes += lineBytes[tailStart - 1];
            tailStart--;
        }

        int omittedLines = tailStart - headCount;
        long omittedBytes = lineBytes[headCount..tailStart].Sum(bytes => (long)bytes);
        string marker = string.Create(
            CultureInfo.InvariantCulture,
            $"--- {omittedLines} transcript lines ({omittedBytes} bytes) omitted ---");

        return string.Join('\n', [.. lines[..headCount], marker, .. lines[tailStart..]]);
    }
}
