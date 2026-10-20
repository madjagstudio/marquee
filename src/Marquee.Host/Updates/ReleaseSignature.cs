using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Marquee.Host.Updates;

/// A release file's signature, kept beside it as `<file>.sig`. A .sig is one line of ASCII:
///
///     marquee-sig-v1 <signature>
///
/// followed by a line feed, where <signature> is the standard, padded Base64 of the 64-byte IEEE P1363 (r || s) ECDSA
/// P-256 signature over the SHA-256 of the file's bytes. The version word is there so that a later format (another
/// curve or hash) can be told apart from this one: a Marquee that only knows v1 refuses anything else rather than
/// guessing. Verify is strict because the text comes from the network: the exact prefix, exactly 88 Base64 characters
/// in the form Sign writes them, and at most one line ending (LF or CRLF). Anything else is false, never an exception.
/// Both directions read the file as a stream, so an installer is hashed without being held in memory.
public static class ReleaseSignature
{
    /// The first word of every .sig this version of Marquee writes and accepts.
    public const string Version = "marquee-sig-v1";

    // P-256: r and s are 32 bytes each, which Base64 writes as 86 characters and "==".
    const int SignatureBytes = 64;
    const int EncodedLength = 88;

    /// Signs the file from its current position to its end and returns the text of its .sig, line feed included.
    public static string Sign(Stream file, ECDsa privateKey)
    {
        var signature = privateKey.SignData(file, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{Version} {Convert.ToBase64String(signature)}\n";
    }

    /// Whether `signatureText` is a well-formed .sig whose signature over the file (from its current position to its
    /// end) verifies against `publicKey`.
    public static bool Verify(Stream file, string signatureText, ECDsa publicKey) =>
        TryParse(signatureText, out var signature)
        && publicKey.VerifyData(file, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    /// A key's fingerprint: the SHA-256 of its public half (the DER SubjectPublicKeyInfo) in lowercase hex. The same for
    /// a private key and its public half, so the release tool can print it at keygen and ReleaseKey can compare it.
    public static string Fingerprint(ECDsa key) =>
        Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();

    static bool TryParse(string text, [NotNullWhen(true)] out byte[]? signature)
    {
        signature = null;

        var line = text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2]
            : text.EndsWith('\n') ? text[..^1]
            : text;

        const string prefix = Version + " ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) || line.Length != prefix.Length + EncodedLength)
            return false;

        var encoded = line[prefix.Length..];
        var bytes = new byte[SignatureBytes];
        if (!Convert.TryFromBase64String(encoded, bytes, out var written) || written != SignatureBytes)
            return false;

        // Convert skips whitespace and ignores the unused bits of the last character, so different texts can decode to
        // the same bytes. Only the one Sign writes is accepted.
        if (!string.Equals(Convert.ToBase64String(bytes), encoded, StringComparison.Ordinal))
            return false;

        signature = bytes;
        return true;
    }
}
