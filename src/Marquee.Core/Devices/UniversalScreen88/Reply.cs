namespace Marquee.Core.Devices.UniversalScreen88;

/// A plaintext reply: [0] echoes the command, [1] is 0xC8 on success, and the body starts at [8].
public readonly record struct Reply(byte Command, bool Ok, byte[] Body)
{
    const byte StatusOk = 0xC8;
    const int BodyOffset = 8;

    public static Reply? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return null;
        var body = data.Length > BodyOffset ? data[BodyOffset..].ToArray() : [];
        return new Reply(data[0], data[1] == StatusOk, body);
    }
}
