using System.Security.Cryptography;
using Marquee.Host.Updates;
using Marquee.ReleaseTool.Tests.TestSupport;

namespace Marquee.ReleaseTool.Tests;

public sealed class ReleaseCommandsTests : IDisposable
{
    const string Password = "a long test password";

    readonly StringWriter output = new();
    readonly StringWriter error = new();

    /// The key built into Marquee, as the tool sees it: some other key until MakeKey makes one, whose public half is
    /// then built in, as the key ceremony does.
    ECDsa builtIn = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose() => builtIn.Dispose();

    int Run(FakePasswords passwords, params string[] args) =>
        new ReleaseCommands(output, error, passwords.Ask, builtIn).Run(args);

    /// A key made with keygen, and its public half as a PEM file beside it; that public half is the built-in key now.
    (string Key, string PublicKey) MakeKey(TempDir dir)
    {
        var key = dir.File("release.pem");
        Assert.Equal(0, Run(new FakePasswords(Password, Password), "keygen", key));
        using var opened = SigningKey.Open(File.ReadAllText(key), Password);
        var publicKey = dir.Write("release.pub.pem", opened.ExportSubjectPublicKeyInfoPem());
        BuildIn(File.ReadAllText(publicKey));
        return (key, publicKey);
    }

    /// Makes `publicPem` the key built into Marquee.
    void BuildIn(string publicPem)
    {
        builtIn.Dispose();
        builtIn = ECDsa.Create();
        builtIn.ImportFromPem(publicPem);
    }

    [Fact]
    public void Check_key_refuses_the_placeholder_without_asking_for_a_password()
    {
        BuildIn(PlaceholderKey.Pem);
        var passwords = new FakePasswords();

        Assert.Equal(1, Run(passwords, "check-key"));

        Assert.Equal("release-key.pub.pem is still the placeholder; run the key ceremony (docs/releasing.md) first" + Environment.NewLine,
            error.ToString());
        Assert.Equal("", output.ToString());
        Assert.Empty(passwords.Prompts);
    }

    [Fact]
    public void Check_key_checks_the_key_embedded_from_the_repos_pem()
    {
        BuildIn(ReleaseKey.Public.ExportSubjectPublicKeyInfoPem());

        Assert.Equal(ReleaseKey.IsPlaceholder ? 1 : 0, Run(new FakePasswords(), "check-key"));
    }

    [Fact]
    public void Check_key_prints_a_test_keys_fingerprint()
    {
        var passwords = new FakePasswords();

        Assert.Equal(0, Run(passwords, "check-key"));

        Assert.Equal(ReleaseSignature.Fingerprint(builtIn) + Environment.NewLine, output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Empty(passwords.Prompts);
    }

    [Fact]
    public void Hashes_writes_sha256sums_beside_the_first_file_with_one_line_per_file()
    {
        using var first = new TempDir();
        using var second = new TempDir();
        var msi = first.Write("Marquee-0.1.0-x64.msi", "abc");
        var zip = second.Write("marquee-0.1.0.zip", "");
        var passwords = new FakePasswords();

        Assert.Equal(0, Run(passwords, "hashes", msi, zip));

        Assert.Equal(new[]
        {
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  Marquee-0.1.0-x64.msi",
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  marquee-0.1.0.zip",
        }, File.ReadAllLines(first.File("SHA256SUMS")));
        Assert.False(File.Exists(second.File("SHA256SUMS")));
        Assert.Equal("", error.ToString());
        Assert.Empty(passwords.Prompts);
    }

    [Fact]
    public void Keygen_writes_an_encrypted_key_and_prints_its_public_half_and_fingerprint()
    {
        using var dir = new TempDir();
        var path = dir.File("release.pem");
        var passwords = new FakePasswords(Password, Password);

        Assert.Equal(0, Run(passwords, "keygen", path));

        Assert.Equal(2, passwords.Prompts.Count);
        var pem = File.ReadAllText(path);
        Assert.StartsWith("-----BEGIN ENCRYPTED PRIVATE KEY-----", pem);
        using var key = SigningKey.Open(pem, Password);
        Assert.Contains(key.ExportSubjectPublicKeyInfoPem(), output.ToString());
        Assert.Contains(ReleaseSignature.Fingerprint(key), output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Keygen_refuses_an_existing_file_before_asking_anything_and_leaves_it_alone()
    {
        using var dir = new TempDir();
        var path = dir.Write("release.pem", "the owner's real key");
        var passwords = new FakePasswords();

        Assert.Equal(1, Run(passwords, "keygen", path));

        Assert.Empty(passwords.Prompts);
        Assert.Equal("the owner's real key", File.ReadAllText(path));
        Assert.Contains("already exists", error.ToString());
    }

    [Fact]
    public void Keygen_refuses_two_different_passwords_and_writes_nothing()
    {
        using var dir = new TempDir();
        var path = dir.File("release.pem");

        Assert.Equal(1, Run(new FakePasswords(Password, Password + "!"), "keygen", path));

        Assert.False(File.Exists(path));
        Assert.Contains("didn't match", error.ToString());
    }

    [Fact]
    public void Keygen_refuses_a_short_password_and_writes_nothing()
    {
        using var dir = new TempDir();
        var path = dir.File("release.pem");
        var passwords = new FakePasswords("short");

        Assert.Equal(1, Run(passwords, "keygen", path));

        Assert.Single(passwords.Prompts);
        Assert.False(File.Exists(path));
        Assert.Contains("at least 12 characters", error.ToString());
    }

    [Fact]
    public void Keygen_refuses_a_folder_that_does_not_exist()
    {
        using var dir = new TempDir();
        var passwords = new FakePasswords();

        Assert.Equal(1, Run(passwords, "keygen", dir.File(@"missing\release.pem")));

        Assert.Empty(passwords.Prompts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keygen_refuses_a_path_inside_a_git_work_tree_before_asking_anything(bool gitFile)
    {
        // A clone has a .git folder at its root; a worktree or a submodule has a .git file there instead.
        using var dir = new TempDir();
        if (gitFile) dir.Write(".git", "gitdir: elsewhere");
        else Directory.CreateDirectory(dir.File(".git"));
        Directory.CreateDirectory(dir.File("tools"));
        var path = dir.File(@"tools\release.pem");
        var passwords = new FakePasswords();

        Assert.Equal(1, Run(passwords, "keygen", path));

        Assert.Empty(passwords.Prompts);
        Assert.False(File.Exists(path));
        Assert.Contains("inside a git work tree", error.ToString());
    }

    [Fact]
    public void Sign_writes_a_sig_beside_each_file_that_verifies_against_the_public_half()
    {
        using var dir = new TempDir();
        var (key, publicKeyPath) = MakeKey(dir);
        var msi = dir.Write("Marquee-0.1.0.msi", "pretend this is an MSI");
        var zip = dir.Write("marquee-0.1.0.zip", "pretend this is a zip");
        var passwords = new FakePasswords(Password);

        Assert.Equal(0, Run(passwords, "sign", key, msi, zip));

        Assert.Single(passwords.Prompts);
        using var publicKey = ECDsa.Create();
        publicKey.ImportFromPem(File.ReadAllText(publicKeyPath));
        foreach (var file in new[] { msi, zip })
        {
            using var stream = File.OpenRead(file);
            Assert.True(ReleaseSignature.Verify(stream, File.ReadAllText(file + ".sig"), publicKey), file);
        }

        Assert.Contains(ReleaseSignature.Fingerprint(publicKey), output.ToString());
    }

    [Fact]
    public void Sign_replaces_an_old_sig()
    {
        using var dir = new TempDir();
        var (key, _) = MakeKey(dir);
        var msi = dir.Write("Marquee.msi", "pretend this is an MSI");
        dir.Write("Marquee.msi.sig", "marquee-sig-v1 stale\n");

        Assert.Equal(0, Run(new FakePasswords(Password), "sign", key, msi));

        Assert.NotEqual("marquee-sig-v1 stale\n", File.ReadAllText(msi + ".sig"));
    }

    [Fact]
    public void Sign_with_the_wrong_password_writes_nothing()
    {
        using var dir = new TempDir();
        var (key, _) = MakeKey(dir);
        var msi = dir.Write("Marquee.msi", "pretend this is an MSI");

        Assert.Equal(1, Run(new FakePasswords("not the password"), "sign", key, msi));

        Assert.False(File.Exists(msi + ".sig"));
        Assert.Contains("Wrong password", error.ToString());
    }

    [Fact]
    public void Sign_refuses_an_unencrypted_key()
    {
        using var dir = new TempDir();
        using var plain = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = dir.Write("plain.pem", plain.ExportPkcs8PrivateKeyPem());
        var msi = dir.Write("Marquee.msi", "pretend this is an MSI");

        Assert.Equal(1, Run(new FakePasswords(Password), "sign", key, msi));

        Assert.False(File.Exists(msi + ".sig"));
    }

    [Fact]
    public void Sign_checks_every_file_exists_before_asking_for_the_password()
    {
        using var dir = new TempDir();
        var (key, _) = MakeKey(dir);
        var msi = dir.Write("Marquee.msi", "pretend this is an MSI");
        var passwords = new FakePasswords();

        Assert.Equal(1, Run(passwords, "sign", key, msi, dir.File("missing.zip")));

        Assert.Empty(passwords.Prompts);
        Assert.False(File.Exists(msi + ".sig"));
        Assert.Contains("missing.zip", error.ToString());
    }

    [Fact]
    public void Sign_refuses_a_key_that_is_not_the_one_built_into_Marquee_and_writes_nothing()
    {
        // An old key, a test key, a superseded key's backup: every installed Marquee would refuse what it signed.
        using var dir = new TempDir();
        var (key, _) = MakeKey(dir);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        BuildIn(other.ExportSubjectPublicKeyInfoPem());
        var msi = dir.Write("Marquee.msi", "pretend this is an MSI");

        Assert.Equal(1, Run(new FakePasswords(Password), "sign", key, msi));

        Assert.False(File.Exists(msi + ".sig"));
        Assert.Contains("not the key built into Marquee", error.ToString());
        Assert.Contains(ReleaseSignature.Fingerprint(other), error.ToString());
    }

    [Fact]
    public void Sign_refuses_while_the_placeholder_is_built_in_before_asking_for_the_password()
    {
        using var dir = new TempDir();
        var (key, _) = MakeKey(dir);
        BuildIn(PlaceholderKey.Pem);
        var msi = dir.Write("Marquee.msi", "pretend this is an MSI");
        var passwords = new FakePasswords();

        Assert.Equal(1, Run(passwords, "sign", key, msi));

        Assert.Empty(passwords.Prompts);
        Assert.False(File.Exists(msi + ".sig"));
        Assert.Contains("placeholder", error.ToString());
    }

    [Fact]
    public void Verify_accepts_a_good_sig()
    {
        using var dir = new TempDir();
        var (key, publicKey) = MakeKey(dir);
        var msi = dir.Write("Marquee.msi", "pretend this is an MSI");
        Assert.Equal(0, Run(new FakePasswords(Password), "sign", key, msi));

        Assert.Equal(0, Run(new FakePasswords(), "verify", publicKey, msi));
    }

    [Fact]
    public void Verify_refuses_a_changed_file_a_missing_sig_and_another_key()
    {
        using var dir = new TempDir();
        var (key, publicKey) = MakeKey(dir);
        var msi = dir.Write("Marquee.msi", "pretend this is an MSI");
        var unsigned = dir.Write("Unsigned.msi", "pretend this is an MSI");
        Assert.Equal(0, Run(new FakePasswords(Password), "sign", key, msi));
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherKey = dir.Write("other.pub.pem", other.ExportSubjectPublicKeyInfoPem());

        Assert.Equal(1, Run(new FakePasswords(), "verify", otherKey, msi));
        Assert.Equal(1, Run(new FakePasswords(), "verify", publicKey, unsigned));
        File.AppendAllText(msi, "!");
        Assert.Equal(1, Run(new FakePasswords(), "verify", publicKey, msi));
    }

    [Fact]
    public void Verify_without_a_public_key_checks_against_the_one_built_into_Marquee()
    {
        using var dir = new TempDir();
        var (key, _) = MakeKey(dir);
        var msi = dir.Write("Marquee.msi", "pretend this is an MSI");
        Assert.Equal(0, Run(new FakePasswords(Password), "sign", key, msi));

        Assert.Equal(0, Run(new FakePasswords(), "verify", msi));
        Assert.Contains($"Good signature: {msi} (key {ReleaseSignature.Fingerprint(builtIn)})", output.ToString());

        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        BuildIn(other.ExportSubjectPublicKeyInfoPem());
        Assert.Equal(1, Run(new FakePasswords(), "verify", msi));
    }

    [Fact]
    public void Verify_without_a_public_key_refuses_while_the_placeholder_is_built_in()
    {
        using var dir = new TempDir();
        var (key, _) = MakeKey(dir);
        var msi = dir.Write("Marquee.msi", "pretend this is an MSI");
        Assert.Equal(0, Run(new FakePasswords(Password), "sign", key, msi));
        BuildIn(PlaceholderKey.Pem);

        Assert.Equal(1, Run(new FakePasswords(), "verify", msi));

        Assert.Contains("placeholder", error.ToString());
    }

    [Fact]
    public void Verify_refuses_a_key_file_that_is_not_a_public_key()
    {
        using var dir = new TempDir();
        var (key, _) = MakeKey(dir);
        var msi = dir.Write("Marquee.msi", "pretend this is an MSI");
        Assert.Equal(0, Run(new FakePasswords(Password), "sign", key, msi));

        Assert.Equal(1, Run(new FakePasswords(), "verify", key, msi));
        Assert.Equal(1, Run(new FakePasswords(), "verify", dir.Write("junk.pem", "not a key"), msi));
    }

    [Theory]
    [InlineData]
    [InlineData("help")]
    [InlineData("keygen")]
    [InlineData("keygen", "a.pem", "b.pem")]
    [InlineData("sign", "key.pem")]
    [InlineData("verify")]
    [InlineData("hashes")]
    [InlineData("check-key", "extra")]
    [InlineData("verify", "key.pub.pem", "a.msi", "b.msi")]
    public void Anything_else_prints_the_usage(params string[] args)
    {
        Assert.Equal(2, Run(new FakePasswords(), args));

        Assert.Contains("keygen <out.pem>", error.ToString());
    }
}
