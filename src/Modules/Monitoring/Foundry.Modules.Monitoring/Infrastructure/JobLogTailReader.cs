using System.Text;

namespace Foundry.Modules.Monitoring.Infrastructure;

/// <summary>
/// Reads the final bytes of a CI job log by streaming its body, so a log of any size is read in bounded memory and
/// never through <see cref="HttpClient.MaxResponseContentBufferSize"/>. The content must come from a response read
/// with <see cref="HttpCompletionOption.ResponseHeadersRead"/>, or the client has already buffered it whole.
/// </summary>
internal static class JobLogTailReader
{
    /// <summary>The most of a job log's final bytes kept. The prompt keeps a smaller tail of this again.</summary>
    public const int JobLogTailBytes = 256 * 1024;

    private const int MaxUtf8ContinuationBytes = 3;

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <param name="content">The log body.</param>
    /// <param name="startsMidLog">Whether the body begins part-way into the log, as a range response does.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<string> ReadAsync(
        HttpContent content,
        bool startsMidLog,
        CancellationToken cancellationToken)
    {
        // Twice the tail, so the kept bytes move back only once per tail's worth read rather than on every read.
        byte[] buffer = new byte[JobLogTailBytes * 2];
        int length = 0;
        long totalRead = 0;

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        while (true)
        {
            if (length == buffer.Length)
            {
                Buffer.BlockCopy(buffer, length - JobLogTailBytes, buffer, 0, JobLogTailBytes);
                length = JobLogTailBytes;
            }

            int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            length += read;
            totalRead += read;
        }

        int start = Math.Max(0, length - JobLogTailBytes);
        bool isCut = startsMidLog || totalRead > JobLogTailBytes;
        return Decode(buffer.AsSpan(start, length - start), isCut);
    }

    private static string Decode(ReadOnlySpan<byte> bytes, bool isCut)
    {
        // A cut can land inside a multi-byte character; its orphaned continuation bytes would decode as a
        // replacement character, so they are dropped. A whole log keeps its first byte but loses any BOM.
        if (isCut)
        {
            int skip = 0;
            while (skip < MaxUtf8ContinuationBytes && skip < bytes.Length && IsContinuationByte(bytes[skip]))
            {
                skip++;
            }

            bytes = bytes[skip..];
        }
        else if (bytes.StartsWith(ByteOrderMark))
        {
            bytes = bytes[ByteOrderMark.Length..];
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static bool IsContinuationByte(byte value) => (value & 0b1100_0000) == 0b1000_0000;
}
