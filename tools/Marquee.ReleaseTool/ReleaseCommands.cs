using System.Security.Cryptography;
using Marquee.Host.Updates;

namespace Marquee.ReleaseTool;

/// The maintainer's release commands. Passwords come from `askPassword` (the console, without echo, in Program; a fake in the
/// tests), are held as char arrays and cleared once used. Exit codes: 0 done, 1 refused or failed, 2 usage.
///
/// `builtInKey` is the public key Marquee is built to trust (ReleaseKey.Public, compiled into the tool from the same
/// release-key.pub.pem). sign refuses any other key, and refuses while that is the placeholder, so a release can only be
/// signed with the key installed copies will accept; verify checks against it unless given a public key. The key
/// ceremony fits that order: keygen, then the new public key into release-key.pub.pem, then sign, which builds the tool
/// with it (docs/releasing.md).
sealed class ReleaseCommands(TextWriter output, TextWriter error, Func<string, char[]> askPassword, ECDsa builtInKey)
{
    /// Long enough that the password, not the 600,000 PBKDF2 rounds, is what stands between a copy of the file and the
    /// key. A password manager's generated password is far longer.
    const int MinimumPasswordLength = 12;

    public int Run(string[] args) => args switch
    {
        ["check-key"] => CheckKey(),
        ["hashes", .. var files] when files.Length > 0 => Hashes(files),
        ["keygen", var path] => Keygen(path),
        ["sign", var keyPath, .. var files] when files.Length > 0 => Sign(keyPath, files),
        ["verify", var file] => Verify(null, file),
        ["verify", var publicKeyPath, var file] => Verify(publicKeyPath, file),
        _ => Usage(),
    };

    int CheckKey()
    {
        if (ReleaseKey.IsPlaceholderKey(builtInKey))
            return Fail("release-key.pub.pem is still the placeholder; run the key ceremony (docs/releasing.md) first");

        output.WriteLine(ReleaseSignature.Fingerprint(builtInKey));
        return 0;
    }

    int Hashes(string[] files)
    {
        try
        {
            // Read every input before writing the manifest, so a missing file leaves no partial SHA256SUMS.
            var lines = files.Select(file =>
            {
                using var stream = File.OpenRead(file);
                return $"{Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()}  {Path.GetFileName(file)}";
            }).ToArray();
            var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(files[0]))!, "SHA256SUMS");
            File.WriteAllLines(path, lines);
            output.WriteLine($"Wrote {path}.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail($"Couldn't write SHA256SUMS: {ex.Message}");
        }
    }

    int Keygen(string path)
    {
        path = Path.GetFullPath(path);
        if (File.Exists(path) || Directory.Exists(path))
            return Fail($"{path} already exists. keygen never overwrites a key; choose a new file name.");
        if (!Directory.Exists(Path.GetDirectoryName(path)))
            return Fail($"The folder for {path} doesn't exist.");
        // In a clone, one `git add .` would publish the key; encrypted or not, it belongs nowhere near the repository.
        if (GitWorkTreeOf(Path.GetDirectoryName(path)!) is { } workTree)
            return Fail($"{path} is inside a git work tree ({workTree}). Write the key outside any repository, e.g. $HOME\\marquee-release-key.pem.");

        var password = askPassword("Password for the new key: ");
        char[]? again = null;
        try
        {
            if (password.Length < MinimumPasswordLength)
                return Fail($"The password must be at least {MinimumPasswordLength} characters.");

            again = askPassword("The same password again: ");
            if (!password.AsSpan().SequenceEqual(again))
                return Fail("The two passwords didn't match. Nothing was written.");

            var key = SigningKey.Create(password);

            // CreateNew, so a file that appeared since the check above is still never overwritten.
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
                writer.Write(key.EncryptedPem);

            output.WriteLine($"Wrote the encrypted key to {path}.");
            output.WriteLine();
            output.WriteLine("Public key, for src/Marquee.Host/Updates/release-key.pub.pem:");
            output.WriteLine();
            output.Write(key.PublicPem);
            output.WriteLine();
            output.WriteLine($"Fingerprint (SHA-256): {key.Fingerprint}");
            output.WriteLine();
            output.WriteLine("Next: put the public key in release-key.pub.pem, store the key file in your password manager and on the offline USB stick, then delete this copy (docs/releasing.md).");
            return 0;
        }
        catch (IOException ex)
        {
            return Fail($"Couldn't write {path}: {ex.Message}");
        }
        finally
        {
            Array.Clear(password);
            if (again is not null)
                Array.Clear(again);
        }
    }

    int Sign(string keyPath, string[] files)
    {
        // Every file is checked before the password is asked for, so a typo costs nothing.
        string[] missing = [.. new[] { keyPath }.Concat(files).Where(file => !File.Exists(file))];
        if (missing.Length > 0)
            return Fail($"Not found: {string.Join(", ", missing)}");
        if (ReleaseKey.IsPlaceholderKey(builtInKey))
            return Fail("This tool was built with the placeholder in release-key.pub.pem, so no installed Marquee would accept what it signed. "
                + "Put the release key's public half in src/Marquee.Host/Updates/release-key.pub.pem first (docs/releasing.md). Nothing was signed.");

        ECDsa key;
        var password = askPassword($"Password for {keyPath}: ");
        try
        {
            key = SigningKey.Open(File.ReadAllText(keyPath), password);
        }
        catch (CryptographicException)
        {
            return Fail($"Wrong password, or {keyPath} is not a key made by keygen. Nothing was signed.");
        }
        finally
        {
            Array.Clear(password);
        }

        using (key)
        using (var publicKey = ECDsa.Create())
        {
            publicKey.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);
            var fingerprint = ReleaseSignature.Fingerprint(publicKey);
            var builtIn = ReleaseSignature.Fingerprint(builtInKey);
            // An old key, a test key or a superseded key's backup would sign files that verify here, and that every
            // installed Marquee refuses.
            if (fingerprint != builtIn)
                return Fail($"{keyPath} is the key {fingerprint}, not the key built into Marquee ({builtIn}), so installed copies "
                    + "would refuse what it signed. Nothing was signed.");
            output.WriteLine($"Key fingerprint (SHA-256): {fingerprint}");

            foreach (var file in files)
            {
                var signaturePath = file + ".sig";
                using (var stream = File.OpenRead(file))
                    File.WriteAllText(signaturePath, ReleaseSignature.Sign(stream, key));

                // Read back what is on disk and check it with the public half alone, as Marquee will.
                if (!Verifies(file, signaturePath, publicKey))
                {
                    File.Delete(signaturePath);
                    return Fail($"The signature written for {file} didn't verify, so it was deleted. Don't publish this release.");
                }

                output.WriteLine($"Signed {file} -> {signaturePath} (checked).");
            }
        }

        return 0;
    }

    /// Checks `file`'s .sig against the public key in `publicKeyPath`, or against the built-in key when that is null.
    int Verify(string? publicKeyPath, string file)
    {
        var signaturePath = file + ".sig";
        string[] missing = [.. new[] { publicKeyPath, file, signaturePath }.OfType<string>().Where(path => !File.Exists(path))];
        if (missing.Length > 0)
            return Fail($"Not found: {string.Join(", ", missing)}");

        if (publicKeyPath is null)
        {
            if (ReleaseKey.IsPlaceholderKey(builtInKey))
                return Fail("This tool was built with the placeholder in release-key.pub.pem, which signs nothing. Give verify a public key PEM.");
            return Verified(file, signaturePath, builtInKey);
        }

        using var publicKey = ECDsa.Create();
        try
        {
            // ImportFromPem takes a public key or an unencrypted private one; refuse the latter so this command is
            // only ever run with what Marquee itself has.
            publicKey.ImportFromPem(File.ReadAllText(publicKeyPath));
            if (HasPrivateHalf(publicKey))
                return Fail($"{publicKeyPath} holds a private key. Give verify the public key (release-key.pub.pem).");
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return Fail($"{publicKeyPath} is not a public key PEM.");
        }

        return Verified(file, signaturePath, publicKey);
    }

    int Verified(string file, string signaturePath, ECDsa publicKey)
    {
        if (!Verifies(file, signaturePath, publicKey))
            return Fail($"BAD signature: {file} does not match {signaturePath} under the key {ReleaseSignature.Fingerprint(publicKey)}.");

        output.WriteLine($"Good signature: {file} (key {ReleaseSignature.Fingerprint(publicKey)}).");
        return 0;
    }

    static bool Verifies(string file, string signaturePath, ECDsa publicKey)
    {
        using var stream = File.OpenRead(file);
        return ReleaseSignature.Verify(stream, File.ReadAllText(signaturePath), publicKey);
    }

    /// The root of the git work tree `folder` is in, or null: the nearest folder, `folder` included, that holds a .git
    /// folder (a clone) or a .git file (a worktree or a submodule).
    static string? GitWorkTreeOf(string folder)
    {
        for (var current = new DirectoryInfo(folder); current is not null; current = current.Parent)
        {
            var git = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return current.FullName;
        }

        return null;
    }

    static bool HasPrivateHalf(ECDsa key)
    {
        try
        {
            key.ExportParameters(includePrivateParameters: true);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    int Fail(string message)
    {
        error.WriteLine(message);
        return 1;
    }

    int Usage()
    {
        error.WriteLine("Marquee release tool: the maintainer's key and signing commands (docs/releasing.md). Not shipped.");
        error.WriteLine();
        error.WriteLine("  check-key                  refuse the placeholder, or print the built-in key's fingerprint");
        error.WriteLine("  hashes <file>...           write SHA256SUMS beside the first file");
        error.WriteLine("  keygen <out.pem>             make the release signing key, encrypted with a password, outside any repository");
        error.WriteLine("  sign <key.pem> <file>...     write <file>.sig beside each file, and check it; the key must be the");
        error.WriteLine("                               one built into Marquee (release-key.pub.pem)");
        error.WriteLine("  verify [<pub.pem>] <file>    check <file>.sig against a public key, or the one built into Marquee");
        return 2;
    }
}
