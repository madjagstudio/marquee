using System.Security.Cryptography;

namespace Marquee.Core.Tests.TestSupport;

static class Packets
{
    static readonly byte[] Key = "slv3tuzx"u8.ToArray();

    /// Decrypts the 504-byte ciphertext at the start of a command packet back to its 500-byte plaintext.
    public static byte[] DecryptHeader(byte[] packet)
    {
        using var des = DES.Create();
        des.Key = Key;
        return des.DecryptCbc(packet.AsSpan(0, 504), Key, PaddingMode.PKCS7);
    }
}
