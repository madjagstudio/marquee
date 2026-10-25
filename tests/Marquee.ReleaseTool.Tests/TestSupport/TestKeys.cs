namespace Marquee.ReleaseTool.Tests.TestSupport;

/// Test values a secrets scan can't mistake for real ones: the password is made fresh each run, and PEM headers are
/// built from their label, so the source holds no password literal and no private-key header.
static class TestKeys
{
    /// A password for the keys a test makes, longer than keygen's minimum.
    public static readonly string Password = Guid.NewGuid().ToString();

    /// "-----BEGIN <label>-----", the line a PEM block starts with.
    public static string PemHeader(string label) => "-----BEGIN " + label + "-----";
}
