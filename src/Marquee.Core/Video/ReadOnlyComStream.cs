using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Marquee.Core.Video;

/// A read-only COM IStream over a .NET stream, so Media Foundation reads a clip through a file Marquee opened (see
/// ClipFile). Media Foundation may call it from its own worker threads, so each call holds a lock. The caller still
/// owns and disposes the stream.
sealed class ReadOnlyComStream(Stream stream) : IStream
{
    const int STGTY_STREAM = 2;
    const int STGM_READ = 0;
    const int STG_E_INVALIDFUNCTION = unchecked((int)0x80030001);
    const int E_NOTIMPL = unchecked((int)0x80004001);

    readonly object _gate = new();

    public void Read(byte[] pv, int cb, IntPtr pcbRead)
    {
        var total = 0;
        lock (_gate)
        {
            // A stream may return fewer bytes than asked before its end; IStream returns fewer only at the end.
            while (total < cb && stream.Read(pv, total, cb - total) is > 0 and var read)
                total += read;
        }

        if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, total);
    }

    public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
    {
        var origin = dwOrigin switch
        {
            0 => SeekOrigin.Begin,
            1 => SeekOrigin.Current,
            2 => SeekOrigin.End,
            _ => throw new COMException("Unknown seek origin.", STG_E_INVALIDFUNCTION),
        };

        long position;
        lock (_gate) position = stream.Seek(dlibMove, origin);
        if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, position);
    }

    public void Stat(out STATSTG pstatstg, int grfStatFlag)
    {
        lock (_gate) pstatstg = new STATSTG { type = STGTY_STREAM, cbSize = stream.Length, grfMode = STGM_READ };
    }

    public void Commit(int grfCommitFlags)
    {
        // Nothing is ever written, so there is nothing to commit.
    }

    public void Write(byte[] pv, int cb, IntPtr pcbWritten) => throw ReadOnly();
    public void SetSize(long libNewSize) => throw ReadOnly();
    public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => throw new COMException(null, E_NOTIMPL);
    public void Revert() => throw new COMException(null, E_NOTIMPL);
    public void LockRegion(long libOffset, long cb, int dwLockType) => throw new COMException(null, STG_E_INVALIDFUNCTION);
    public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new COMException(null, STG_E_INVALIDFUNCTION);
    public void Clone(out IStream ppstm) => throw new COMException(null, E_NOTIMPL);

    static COMException ReadOnly() => new("The clip is opened read-only.", STG_E_INVALIDFUNCTION);
}
