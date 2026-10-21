using System.Buffers.Binary;

namespace Marquee.Host.SensorFeed;

/// The sensor feed's wire format. Each connection gets one message and is closed: the message's length in bytes as a
/// 4-byte little-endian unsigned integer, then that many bytes of UTF-8 JSON.
///
/// Values: {"version":1,"now":"2026-10-20T12:00:10+00:00","truncated":false,"values":{"cpu.temp":{"number":52.5,
/// "stale":false},"cpu.name":{"text":"Ryzen 9","stale":true}}}
///
/// History: {"version":1,"now":"…","truncated":false,"history":{"cpu.temp":[50,50.5]}}
///
/// Keys are written in ordinal order. A reader refuses any version but its own; the tray and the editor ship together,
/// so a mismatch means a half-finished update, and the editor falls back to reading sensors itself.
///
/// Over its cap, a message is cut down rather than refused, and says so with "truncated":true:
/// - values keep whole keys, taken in ordinal order; a key that would take the message over the cap is left out, and
///   later, smaller keys can still fit, so the same store always gives the same message;
/// - history keeps the same number of newest points for every key, as many as fit, so the oldest points go first. In
///   the unreachable case that the keys alone don't fit, the history is sent empty.
public static class SensorFeedFormat
{
    /// The format this build writes and reads.
    public const int Version = 1;

    /// The largest values message, in bytes of JSON. Custom values (at most 1000 keys of 256 characters by default)
    /// stay under it in any script, being written as UTF-8 rather than escaped, unless their text is mostly characters
    /// outside Unicode's basic plane, such as emoji, which JSON writers still escape to 12 bytes each.
    public const int MaxValuesBytes = 1024 * 1024;

    /// The largest history message, in bytes of JSON: several hundred keys of 600 points each, depending on their digits.
    public const int MaxHistoryBytes = 8 * 1024 * 1024;

    /// The length prefix's size.
    internal const int PrefixBytes = 4;

    /// The JSON after its length.
    internal static byte[] Frame(byte[] json)
    {
        var frame = new byte[PrefixBytes + json.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)json.Length);
        json.CopyTo(frame, PrefixBytes);
        return frame;
    }
}
