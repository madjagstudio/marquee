using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Marquee.Host.Tests.TestSupport;
using Marquee.Host.Updates;

namespace Marquee.Host.Tests;

public class ReleaseKeyTests
{
    /// A whole PEM private key block of any kind (PKCS#8, encrypted PKCS#8, SEC1 "EC", RSA): the header, a Base64 body
    /// and the matching footer. A header alone, as in a test that checks what the release tool writes, isn't a key.
    static readonly Regex PrivateKeyPem = new(@"-----BEGIN ([A-Z ]*)PRIVATE KEY-----[A-Za-z0-9+/=\s]{40,}-----END \1PRIVATE KEY-----",
        RegexOptions.CultureInvariant);

    static ECDsa Placeholder() => PlaceholderKey.Open();

    [Fact]
    public void The_built_in_key_is_a_P256_public_key_with_no_private_half()
    {
        var key = ReleaseKey.Public;

        Assert.Equal(256, key.KeySize);
        Assert.Equal(ECCurve.NamedCurves.nistP256.Oid.Value, key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value);
        Assert.ThrowsAny<CryptographicException>(() => key.ExportParameters(includePrivateParameters: true));
    }

    [Fact]
    public void The_built_in_key_is_the_one_in_release_key_pub_pem()
    {
        var pem = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Marquee.Host", "Updates", "release-key.pub.pem"));
        using var key = ECDsa.Create();
        key.ImportFromPem(pem);

        Assert.Equal(ReleaseSignature.Fingerprint(key), ReleaseKey.Fingerprint);
    }

    [Fact]
    public void The_placeholder_public_key_is_the_placeholder()
    {
        using var key = Placeholder();

        Assert.True(ReleaseKey.IsPlaceholderKey(key));
    }

    [Fact]
    public void Any_other_key_is_not_the_placeholder()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.False(ReleaseKey.IsPlaceholderKey(key));
    }

    [Fact]
    public void IsPlaceholder_is_true_exactly_while_the_built_in_key_is_the_placeholder()
    {
        // True until the owner's key ceremony replaces release-key.pub.pem, false after, with no test to edit.
        using var placeholder = Placeholder();

        Assert.Equal(ReleaseSignature.Fingerprint(placeholder) == ReleaseKey.Fingerprint, ReleaseKey.IsPlaceholder);
    }

    /// The placeholder's private half was discarded when it was made and the owner's key lives offline
    /// (docs/releasing.md), so no private key belongs anywhere in the repository; a secret scanner would flag one.
    /// Build output and dot folders (.git, .cache, .vs) are skipped, and so are files too big to be a key file.
    [Fact]
    public void No_private_key_is_in_the_repository()
    {
        var root = RepositoryRoot();
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "out", "TestResults" };

        var keys = Files(new DirectoryInfo(root))
            .Where(file => file.Length <= 1024 * 1024 && PrivateKeyPem.IsMatch(File.ReadAllText(file.FullName)))
            .Select(file => Path.GetRelativePath(root, file.FullName))
            .ToList();

        Assert.Empty(keys);

        IEnumerable<FileInfo> Files(DirectoryInfo folder) =>
            folder.EnumerateFiles().Concat(folder.EnumerateDirectories()
                .Where(child => !child.Name.StartsWith('.') && !skipped.Contains(child.Name))
                .SelectMany(Files));
    }

    static string RepositoryRoot()
    {
        // The test runs from tests/Marquee.Host.Tests/bin/<config>/<tfm>; the repository root has Marquee.sln.
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Marquee.sln")))
            folder = folder.Parent;
        Assert.NotNull(folder);
        return folder.FullName;
    }
}
