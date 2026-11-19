namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

/// <summary>
/// A read-only, unseekable stream over the bytes <c>[start, end)</c> of a generated log whose byte at position
/// <c>i</c> is <see cref="ByteAt"/>. It lets a test serve a log far larger than it could hold in memory.
/// </summary>
internal sealed class GeneratedLogStream(long start, long end) : Stream
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

    public static byte ByteAt(long position) => (byte)('a' + (position % 26));

    public static string TextOf(long start, long end)
    {
        char[] text = new char[end - start];
        for (long position = start; position < end; position++)
        {
            text[position - start] = (char)ByteAt(position);
        }

        return new string(text);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int count = (int)Math.Min(buffer.Length, end - _position);
        for (int index = 0; index < count; index++)
        {
            buffer[index] = ByteAt(_position + index);
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
