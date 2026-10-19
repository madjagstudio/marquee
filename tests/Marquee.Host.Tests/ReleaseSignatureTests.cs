using System.Security.Cryptography;
using System.Text;
using Marquee.Host.Tests.TestSupport;
using Marquee.Host.Updates;

namespace Marquee.Host.Tests;

public class ReleaseSignatureTests
{
    static readonly byte[] Installer = Encoding.UTF8.GetBytes("pretend this is an MSI");

    static ECDsa NewKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    static string Sign(byte[] file, ECDsa key) => ReleaseSignature.Sign(new MemoryStream(file), key);

    static bool Verify(byte[] file, string signature, ECDsa key) => ReleaseSignature.Verify(new MemoryStream(file), signature, key);

    /// The Base64 part of a .sig.
    static string Encoded(string signature) => signature.TrimEnd('\n')["marquee-sig-v1 ".Length..];

    [Fact]
    public void A_signed_file_verifies()
    {
        using var key = NewKey();

        Assert.True(Verify(Installer, Sign(Installer, key), key));
    }

    [Fact]
    public void It_verifies_with_the_public_half_alone()
    {
        using var key = NewKey();
        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);

        Assert.True(Verify(Installer, Sign(Installer, key), publicKey));
    }

    [Fact]
    public void A_sig_is_one_line_with_the_version_and_a_64_byte_signature()
    {
        using var key = NewKey();

        var signature = Sign(Installer, key);

        Assert.StartsWith("marquee-sig-v1 ", signature);
        Assert.EndsWith("\n", signature);
        Assert.Single(signature.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(64, Convert.FromBase64String(Encoded(signature)).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(21)]
    public void A_flipped_byte_in_the_file_fails(int at)
    {
        using var key = NewKey();
        var signature = Sign(Installer, key);

        var tampered = Installer.ToArray();
        tampered[at] ^= 0x01;

        Assert.False(Verify(tampered, signature, key));
    }

    [Fact]
    public void A_longer_or_shorter_file_fails()
    {
        using var key = NewKey();
        var signature = Sign(Installer, key);

        Assert.False(Verify([.. Installer, 0], signature, key));
        Assert.False(Verify(Installer[..^1], signature, key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(63)]
    public void A_flipped_byte_in_the_signature_fails(int at)
    {
        using var key = NewKey();
        var bytes = Convert.FromBase64String(Encoded(Sign(Installer, key)));

        bytes[at] ^= 0x01;

        Assert.False(Verify(Installer, $"marquee-sig-v1 {Convert.ToBase64String(bytes)}\n", key));
    }

    [Fact]
    public void A_signature_from_another_key_fails()
    {
        using var key = NewKey();
        using var other = NewKey();

        Assert.False(Verify(Installer, Sign(Installer, other), key));
    }

    // "{sig}" stands for the Base64 of a good signature over Installer.
    [Theory]
    [InlineData("marquee-sig-v1 {sig}")]
    [InlineData("marquee-sig-v1 {sig}\n")]
    [InlineData("marquee-sig-v1 {sig}\r\n")]
    public void A_good_sig_verifies_with_or_without_one_line_ending(string text)
    {
        using var key = NewKey();
        var signature = text.Replace("{sig}", Encoded(Sign(Installer, key)), StringComparison.Ordinal);

        Assert.True(Verify(Installer, signature, key));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\n")]
    [InlineData("marquee-sig-v1")]
    [InlineData("marquee-sig-v1 ")]
    [InlineData("marquee-sig-v1 \n")]
    [InlineData("{sig}")]
    [InlineData("marquee-sig-v2 {sig}")]
    [InlineData("marquee-sig-v0 {sig}")]
    [InlineData("marquee-sig {sig}")]
    [InlineData("MARQUEE-SIG-V1 {sig}")]
    [InlineData("other-sig-v1 {sig}")]
    [InlineData("marquee-sig-v1  {sig}")]
    [InlineData("marquee-sig-v1\t{sig}")]
    [InlineData(" marquee-sig-v1 {sig}")]
    [InlineData("﻿marquee-sig-v1 {sig}")]
    [InlineData("marquee-sig-v1 {sig} ")]
    [InlineData("marquee-sig-v1 {sig}\n\n")]
    [InlineData("marquee-sig-v1 {sig}\r")]
    [InlineData("marquee-sig-v1 {sig}\nmarquee-sig-v1 {sig}\n")]
    [InlineData("marquee-sig-v1 {sig}AAAA")]
    [InlineData("marquee-sig-v1 !!!!not-base64!!!!")]
    [InlineData("marquee-sig-v1 {short}")]
    [InlineData("marquee-sig-v1 {long}")]
    [InlineData("marquee-sig-v1 {spaced}")]
    [InlineData("marquee-sig-v1 {unpadded}")]
    [InlineData("marquee-sig-v1 {der}")]
    public void A_malformed_sig_is_false_without_throwing(string text)
    {
        using var key = NewKey();
        var good = Encoded(Sign(Installer, key));
        var signature = text
            .Replace("{sig}", good, StringComparison.Ordinal)
            .Replace("{short}", Convert.ToBase64String(new byte[32]), StringComparison.Ordinal)
            .Replace("{long}", Convert.ToBase64String(new byte[65]), StringComparison.Ordinal)
            .Replace("{spaced}", good[..40] + " " + good[40..^1], StringComparison.Ordinal)
            .Replace("{unpadded}", good.TrimEnd('='), StringComparison.Ordinal)
            .Replace("{der}", Convert.ToBase64String(key.SignData(Installer, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)), StringComparison.Ordinal);

        Assert.False(Verify(Installer, signature, key));
    }

    [Fact]
    public void Base64_with_its_unused_bits_set_is_refused_although_it_decodes_to_the_same_signature()
    {
        using var key = NewKey();
        var good = Encoded(Sign(Installer, key));

        // 64 bytes are 86 Base64 characters and "==": the last character before the padding carries 4 unused bits.
        // Setting one gives text that Convert still decodes to the same bytes, but it is not what Sign writes.
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var last = alphabet.IndexOf(good[85]);
        var odd = good[..85] + alphabet[last | 1] + "==";
        Assert.Equal(Convert.FromBase64String(good), Convert.FromBase64String(odd));

        Assert.False(Verify(Installer, $"marquee-sig-v1 {odd}\n", key));
    }

    [Fact]
    public void An_installer_sized_file_is_signed_and_verified_as_a_stream()
    {
        const long size = 50L * 1024 * 1024;
        using var key = NewKey();

        var signature = ReleaseSignature.Sign(new PatternStream(size), key);

        Assert.True(ReleaseSignature.Verify(new PatternStream(size), signature, key));
        Assert.False(ReleaseSignature.Verify(new PatternStream(size, flipAt: size - 1), signature, key));
    }

    [Fact]
    public void Signing_twice_gives_two_signatures_that_both_verify()
    {
        // ECDSA signatures are randomized, so a test can't compare a .sig with a stored one; it can only verify it.
        using var key = NewKey();

        var first = Sign(Installer, key);
        var second = Sign(Installer, key);

        Assert.NotEqual(first, second);
        Assert.True(Verify(Installer, first, key));
        Assert.True(Verify(Installer, second, key));
    }

    [Fact]
    public void The_fingerprint_is_the_SHA256_of_the_public_key_info_in_lowercase_hex()
    {
        using var key = NewKey();
        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);
        using var other = NewKey();

        var expected = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();

        Assert.Equal(expected, ReleaseSignature.Fingerprint(key));
        Assert.Equal(expected, ReleaseSignature.Fingerprint(publicKey));
        Assert.NotEqual(expected, ReleaseSignature.Fingerprint(other));
    }
}
