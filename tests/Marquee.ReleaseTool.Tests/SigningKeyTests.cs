using System.Formats.Asn1;
using System.Security.Cryptography;
using Marquee.Host.Updates;
using Marquee.ReleaseTool.Tests.TestSupport;

namespace Marquee.ReleaseTool.Tests;

public class SigningKeyTests
{
    static readonly string Password = TestKeys.Password;

    static readonly byte[] Installer = "pretend this is an MSI"u8.ToArray();

    [Fact]
    public void A_new_key_opens_with_its_password_and_its_public_half_verifies_what_it_signs()
    {
        var created = SigningKey.Create(Password);

        using var key = SigningKey.Open(created.EncryptedPem, Password);
        using var publicKey = ECDsa.Create();
        publicKey.ImportFromPem(created.PublicPem);

        var signature = ReleaseSignature.Sign(new MemoryStream(Installer), key);
        Assert.True(ReleaseSignature.Verify(new MemoryStream(Installer), signature, publicKey));
        Assert.Equal(ReleaseSignature.Fingerprint(publicKey), created.Fingerprint);
        Assert.Equal(256, key.KeySize);
    }

    [Fact]
    public void The_wrong_password_does_not_open_it()
    {
        var created = SigningKey.Create(Password);

        Assert.ThrowsAny<CryptographicException>(() => SigningKey.Open(created.EncryptedPem, "a long test passwore"));
        Assert.ThrowsAny<CryptographicException>(() => SigningKey.Open(created.EncryptedPem, ""));
    }

    [Fact]
    public void The_key_file_holds_only_the_encrypted_key_and_the_public_half_only_the_public_key()
    {
        var created = SigningKey.Create(Password);

        Assert.StartsWith(TestKeys.PemHeader("ENCRYPTED PRIVATE KEY"), created.EncryptedPem);
        Assert.DoesNotContain(TestKeys.PemHeader("PRIVATE KEY"), created.EncryptedPem);
        Assert.DoesNotContain(TestKeys.PemHeader("EC PRIVATE KEY"), created.EncryptedPem);
        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", created.PublicPem);
        Assert.DoesNotContain("PRIVATE", created.PublicPem);
    }

    [Fact]
    public void The_key_is_encrypted_with_AES_256_and_600000_rounds_of_PBKDF2_SHA256()
    {
        var created = SigningKey.Create(Password);
        var fields = PemEncoding.Find(created.EncryptedPem);
        var der = Convert.FromBase64String(created.EncryptedPem[fields.Base64Data]);

        // EncryptedPrivateKeyInfo (RFC 5958) with PBES2 parameters (RFC 8018).
        var info = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
        var algorithm = info.ReadSequence();
        Assert.Equal("1.2.840.113549.1.5.13", algorithm.ReadObjectIdentifier()); // PBES2
        var pbes2 = algorithm.ReadSequence();
        var kdf = pbes2.ReadSequence();
        Assert.Equal("1.2.840.113549.1.5.12", kdf.ReadObjectIdentifier()); // PBKDF2
        var kdfParameters = kdf.ReadSequence();
        Assert.True(kdfParameters.ReadOctetString().Length >= 16, "The salt is shorter than 16 bytes.");
        Assert.Equal(600_000, (int)kdfParameters.ReadInteger());
        if (kdfParameters.PeekTag() == Asn1Tag.Integer)
            kdfParameters.ReadInteger(); // optional key length
        Assert.Equal("1.2.840.113549.2.9", kdfParameters.ReadSequence().ReadObjectIdentifier()); // HMAC-SHA256
        Assert.Equal("2.16.840.1.101.3.4.1.42", pbes2.ReadSequence().ReadObjectIdentifier()); // AES-256-CBC
    }

    [Fact]
    public void An_unencrypted_key_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var plain = key.ExportPkcs8PrivateKeyPem();

        Assert.ThrowsAny<CryptographicException>(() => SigningKey.Open(plain, Password));
    }
}
