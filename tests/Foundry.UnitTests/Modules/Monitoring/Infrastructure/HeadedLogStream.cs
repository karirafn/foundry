namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

/// <summary>
/// A read-only, unseekable stream over the bytes <c>[start, end)</c> of a log that opens with <c>head</c> and continues
/// with <see cref="GeneratedLogStream"/> filler, so a test can serve a large log whose early lines it chose.
/// </summary>
internal sealed class HeadedLogStream(byte[] head, long start, long end) : Stream
{
    private long _position = start;

    public long BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int count = (int)Math.Min(buffer.Length, end - _position);
        for (int index = 0; index < count; index++)
        {
            long position = _position + index;
            buffer[index] = position < head.Length ? head[position] : GeneratedLogStream.ByteAt(position);
        }

        _position += count;
        BytesRead += count;
        return count;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
