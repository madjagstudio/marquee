using System.Security.Cryptography;

namespace Marquee.Host.Updates;

/// The one public key Marquee trusts for updates, built in from release-key.pub.pem (a SubjectPublicKeyInfo PEM,
/// embedded in this assembly). The owner's matching private key never leaves their PC (docs/releasing.md).
///
/// PLACEHOLDER. Until the owner's key ceremony, release-key.pub.pem holds a placeholder public key whose private half
/// was discarded the moment the pair was made, so nothing anyone has can sign for it, and no private key is committed
/// anywhere (tests sign with keys they make in memory). IsPlaceholder is true while that key is built in, and the
/// update check must refuse to install anything then: it checks IsPlaceholder before it offers an update and again
/// before it runs msiexec, whatever the signature says. The ceremony replaces only the PEM file; the placeholder's
/// fingerprint stays here, so a build that still carries the placeholder by mistake knows it has no release key.
public static class ReleaseKey
{
    /// ReleaseSignature.Fingerprint of the placeholder public key that release-key.pub.pem holds before the ceremony.
    const string PlaceholderFingerprint = "1e8951eade8ee46cede93608985629cd5e3167a1f914f7cd75ee31b5b20ba518";

    static readonly Lazy<ECDsa> key = new(Load);

    static readonly Lazy<string> fingerprint = new(() => ReleaseSignature.Fingerprint(key.Value));

    /// The built-in public key, shared for the life of the process: don't dispose it.
    public static ECDsa Public => key.Value;

    /// The built-in key's fingerprint (ReleaseSignature.Fingerprint), for the log.
    public static string Fingerprint => fingerprint.Value;

    /// Whether the built-in key is still the placeholder, which must never be trusted for an install.
    public static bool IsPlaceholder => Fingerprint == PlaceholderFingerprint;

    /// Whether `key` is the placeholder public key.
    public static bool IsPlaceholderKey(ECDsa key) => ReleaseSignature.Fingerprint(key) == PlaceholderFingerprint;

    static ECDsa Load()
    {
        using var stream = typeof(ReleaseKey).Assembly.GetManifestResourceStream("release-key.pub.pem")
            ?? throw new InvalidOperationException("release-key.pub.pem is not embedded in Marquee.Host.");
        using var reader = new StreamReader(stream);
        var key = ECDsa.Create();
        key.ImportFromPem(reader.ReadToEnd());
        return key;
    }
}
