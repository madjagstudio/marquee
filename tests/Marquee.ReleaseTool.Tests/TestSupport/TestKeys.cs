using System.Security.Cryptography;

namespace Marquee.ReleaseTool.Tests.TestSupport;

/// A per-run password for generated test keys, and checks for the PEM files the release tool writes.
static class TestKeys
{
    /// A password for the keys a test makes, longer than keygen's minimum.
    public static readonly string Password = Guid.NewGuid().ToString();

    /// Checks that the file contains one PEM block with the expected label and no other content.
    public static void AssertSinglePem(string expectedLabel, string pem)
    {
        var fields = PemEncoding.Find(pem);
        Assert.Equal(expectedLabel, pem[fields.Label]);
        Assert.Equal(pem.Trim(), pem[fields.Location]);
    }
}
