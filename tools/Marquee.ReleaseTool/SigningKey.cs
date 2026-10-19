using System.Security.Cryptography;
using Marquee.Host.Updates;

namespace Marquee.ReleaseTool;

/// The release signing key: an ECDSA P-256 key that only ever exists on disk as a password-encrypted PKCS #8 PEM
/// (AES-256-CBC, its key derived with 600,000 rounds of PBKDF2-HMAC-SHA256, OWASP's figure for that hash). It is
/// decrypted in memory to sign and never written out in the clear.
static class SigningKey
{
    static readonly PbeParameters Encryption = new(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000);

    /// Makes a new key and returns it encrypted with `password`, with its public half and fingerprint.
    public static NewSigningKey Create(ReadOnlySpan<char> password)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new(
            key.ExportEncryptedPkcs8PrivateKeyPem(password, Encryption) + "\n",
            key.ExportSubjectPublicKeyInfoPem() + "\n",
            ReleaseSignature.Fingerprint(key));
    }

    /// Decrypts a key Create made. A wrong password, or a file that isn't an encrypted P-256 private key (an
    /// unencrypted one included: the tool never accepts a key that was stored in the clear), throws
    /// CryptographicException.
    public static ECDsa Open(string encryptedPem, ReadOnlySpan<char> password)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportFromEncryptedPem(encryptedPem, password);
            if (key.KeySize != 256)
                throw new CryptographicException("The key is not a P-256 key.");
            return key;
        }
        catch (ArgumentException ex)
        {
            key.Dispose();
            throw new CryptographicException("The file is not an encrypted private key.", ex);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}

/// A key Create made: the PEM to keep (encrypted), the public key PEM to build into Marquee, and its fingerprint.
sealed record NewSigningKey(string EncryptedPem, string PublicPem, string Fingerprint);
