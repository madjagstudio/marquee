using System.Security.Cryptography;

namespace Marquee.ReleaseTool.Tests.TestSupport;

/// The original placeholder public key, shared with Marquee.Host's tests to check that the release tool refuses it.
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
