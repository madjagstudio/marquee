namespace Marquee.Host.Tests.TestSupport;

/// A read-only, forward-only stream of `length` bytes made up as it is read, so a test can hash an installer-sized file
/// without holding it in memory or on disk. The bytes depend only on their position, except that the byte at `flipAt`
/// (if any) has its bits inverted, which makes a file that differs from the plain one in exactly one place.
sealed class PatternStream(long length, long flipAt = -1) : Stream
{
    long position;

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
        var count = (int)Math.Min(buffer.Length, length - position);
        for (var i = 0; i < count; i++)
        {
            var at = position + i;
            var value = (byte)((at * 31) ^ (at >> 11));
            buffer[i] = at == flipAt ? (byte)~value : value;
        }

        position += count;
        return count;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
