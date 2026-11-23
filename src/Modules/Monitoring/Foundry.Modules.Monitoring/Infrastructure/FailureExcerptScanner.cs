using System.Text;
using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Infrastructure;

/// <summary>
/// Collects the lines near the start of a CI job log that look like failures, each with the lines after it as
/// context for a stack trace. A test runner can print its failure early and then dump enough output to push it out
/// of the log's tail, so these lines are read beside the tail. The log is fed in chunks as it streams; memory stays
/// bounded by <see cref="MaxExcerptBytes"/> plus one <see cref="MaxLineBytes"/> line.
/// </summary>
internal sealed partial class FailureExcerptScanner
{
    /// <summary>The most of a log's first bytes scanned.</summary>
    public const int ScanBytes = 8 * 1024 * 1024;

    /// <summary>The most excerpt text kept, counting each line's newline; the earliest matches win.</summary>
    public const int MaxExcerptBytes = 32 * 1024;

    /// <summary>The most of one line's bytes kept; a longer line keeps only its start.</summary>
    public const int MaxLineBytes = 4 * 1024;

    private const int ContextLines = 10;
    private const int MaxUtf8SequenceBytes = 4;

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    private readonly byte[] _line = new byte[MaxLineBytes];
    private readonly List<JobLogLine> _excerpt = [];
    private int _lineLength;
    private long _lineStart;
    private long _lineNumber = 1;
    private long _offset;
    private int _contextLeft;
    private int _excerptBytes;
    private bool _isFull;

    /// <summary>Whether the scan has reached its byte cap or filled its excerpt, so further bytes are ignored.</summary>
    public bool IsDone => _isFull || _offset >= ScanBytes;

    /// <summary>Scans a log body from its first byte until the scan is done or the body ends.</summary>
    public static async Task<IReadOnlyList<JobLogLine>> ScanAsync(HttpContent content, CancellationToken cancellationToken)
    {
        FailureExcerptScanner scanner = new();
        byte[] buffer = new byte[81920];

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        while (!scanner.IsDone)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            scanner.Append(buffer.AsSpan(0, read));
        }

        return scanner.Complete();
    }

    /// <summary>Feeds the log's next bytes, in order from the log's first byte.</summary>
    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (IsDone)
        {
            return;
        }

        if (_offset == 0 && bytes.StartsWith(ByteOrderMark))
        {
            bytes = bytes[ByteOrderMark.Length..];
            _offset = ByteOrderMark.Length;
            _lineStart = _offset;
        }

        while (bytes.Length > 0 && !IsDone)
        {
            int room = (int)Math.Min(bytes.Length, ScanBytes - _offset);
            ReadOnlySpan<byte> scannable = bytes[..room];
            int newline = scannable.IndexOf((byte)'\n');
            ReadOnlySpan<byte> part = newline < 0 ? scannable : scannable[..newline];

            int kept = Math.Min(part.Length, _line.Length - _lineLength);
            part[..kept].CopyTo(_line.AsSpan(_lineLength));
            _lineLength += kept;
            _offset += part.Length;

            if (newline < 0)
            {
                break;
            }

            _offset++;
            EndLine();
            bytes = bytes[(newline + 1)..];
        }
    }

    /// <summary>Ends the scan, taking a last line that has no newline, and returns the excerpt in log order.</summary>
    public IReadOnlyList<JobLogLine> Complete()
    {
        if (_offset > _lineStart && !_isFull)
        {
            EndLine();
        }

        return _excerpt;
    }

    private void EndLine()
    {
        string text = Decode(_line.AsSpan(0, _lineLength));
        bool isMarker = MarkerPattern().IsMatch(text);

        if (isMarker || _contextLeft > 0)
        {
            Keep(text);
            _contextLeft = isMarker ? ContextLines : _contextLeft - 1;
        }

        _lineLength = 0;
        _lineStart = _offset;
        _lineNumber++;
    }

    private void Keep(string text)
    {
        int bytes = Encoding.UTF8.GetByteCount(text) + 1;
        if (_excerptBytes + bytes > MaxExcerptBytes)
        {
            _isFull = true;
            return;
        }

        _excerpt.Add(new JobLogLine(_lineNumber, _lineStart, text));
        _excerptBytes += bytes;
    }

    // A line cut at MaxLineBytes can end inside a multi-byte character, whose partial bytes would decode as a
    // replacement character, so they are dropped.
    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == MaxLineBytes)
        {
            int lead = bytes.Length - 1;
            while (lead > bytes.Length - MaxUtf8SequenceBytes && IsContinuationByte(bytes[lead]))
            {
                lead--;
            }

            if (lead + SequenceLength(bytes[lead]) > bytes.Length)
            {
                bytes = bytes[..lead];
            }
        }

        if (bytes.EndsWith("\r"u8))
        {
            bytes = bytes[..^1];
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static bool IsContinuationByte(byte value) => (value & 0b1100_0000) == 0b1000_0000;

    private static int SequenceLength(byte lead) => lead switch
    {
        >= 0b1111_0000 => 4,
        >= 0b1110_0000 => 3,
        >= 0b1100_0000 => 2,
        _ => 1,
    };

    [GeneratedRegex(
        @"\b(failed|failure|error|exception|panic)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex MarkerPattern();
}
