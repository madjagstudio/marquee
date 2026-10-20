using System.Security.Cryptography;

namespace Marquee.Host.Tests.TestSupport;

/// The placeholder public key, as release-key.pub.pem holds it until the owner's key ceremony. It is copied here so the
/// tests still know the placeholder after the ceremony replaces that file. Its private half was never kept, so nothing
/// anyone has can sign for it: the update tests use it to show that the placeholder refuses every update.
static class PlaceholderKey
{
    public const string Pem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE1n7ApjJ4ivIMf/aSV0AcvX6Spyak
        QBUuCDS5/tacJiIMu243kyZt8QNvksvOIr066rwhVCTV2lto47b8jdsXPQ==
        -----END PUBLIC KEY-----
        """;

    public static ECDsa Open()
    {
        var key = ECDsa.Create();
        key.ImportFromPem(Pem);
        return key;
    }
}
