using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Marquee.Core.Devices.UniversalScreen88;

/// Builds 512-byte command packets: 504 bytes of DES-CBC ciphertext, 6 zero bytes, then A1 1A.
public sealed class HeaderBuilder
{
    public const int PlaintextLength = 500;
    public const int PacketLength = 512;
    public const int MaxParameterLength = PlaintextLength - 8;

    static readonly byte[] Key = "slv3tuzx"u8.ToArray();

    readonly Func<long> _clockMs;
    uint _lastTimestamp;
    bool _hasTimestamp;

    // Milliseconds since boot, not a per-process Stopwatch: the panel may expect timestamps to keep
    // increasing across host processes, and a Stopwatch restarts near zero on every CLI run.
    public HeaderBuilder() : this(() => Environment.TickCount64) { }

    public HeaderBuilder(Func<long> clockMs) => _clockMs = clockMs;

    public byte[] Build(Command command, ReadOnlySpan<byte> parameters = default) =>
        Encrypt(BuildPlaintext(command, parameters));

    internal byte[] BuildPlaintext(Command command, ReadOnlySpan<byte> parameters)
    {
        if (parameters.Length > MaxParameterLength)
            throw new ArgumentException($"At most {MaxParameterLength} parameter bytes fit in a header.", nameof(parameters));

        var plain = new byte[PlaintextLength];
        plain[0] = (byte)command;
        plain[2] = 0x1A;
        plain[3] = 0x6D;
        BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(4), NextTimestamp());
        parameters.CopyTo(plain.AsSpan(8));
        return plain;
    }

    uint NextTimestamp()
    {
        var now = (uint)_clockMs();
        var timestamp = _hasTimestamp && now <= _lastTimestamp ? _lastTimestamp + 1 : now;
        _lastTimestamp = timestamp;
        _hasTimestamp = true;
        return timestamp;
    }

    static byte[] Encrypt(byte[] plaintext)
    {
        using var des = DES.Create();
        des.Key = Key;
        var cipher = des.EncryptCbc(plaintext, Key, PaddingMode.PKCS7);

        var packet = new byte[PacketLength];
        cipher.CopyTo(packet, 0);
        packet[510] = 0xA1;
        packet[511] = 0x1A;
        return packet;
    }
}
