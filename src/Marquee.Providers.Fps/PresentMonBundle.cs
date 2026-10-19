using System.Reflection;
using System.Security.Cryptography;

namespace Marquee.Providers.Fps;

/// The PresentMon exe that ships with Marquee. Its file name and SHA-256 are set once, in PresentMon.targets, which
/// bundles the exe and passes both here as assembly metadata, so the build and the code can't disagree about them.
///
/// The hash covers the exe, not the DLLs it loads. PresentMon 2.6.0 imports only kernel32 and delay-loads advapi32,
/// shell32, user32 and tdh. tdh isn't one of Windows' KnownDLLs and the exe sets no DependentLoadFlags, so Windows looks
/// for it in PresentMon's own folder before System32, as it does for the DLLs those system DLLs load in turn: a tdh.dll
/// put in that folder would run inside PresentMon, elevated. Installed under Program Files, the folder only
/// administrators can write to. Run from a folder the user can write to (a build, an unzipped copy), whoever can write
/// there can as easily replace marquee.exe's own DLLs, so a check on PresentMon's folder would close nothing.
public static class PresentMonBundle
{
    public static string FileName { get; } = Metadata("PresentMonFileName");

    /// Lower-case hex.
    public static string Sha256 { get; } = Metadata("PresentMonSha256").ToLowerInvariant();

    /// Where the build puts it: a PresentMon folder next to the app.
    public static string DefaultPath { get; } = Path.Combine(AppContext.BaseDirectory, "PresentMon", FileName);

    /// Opens the exe so that it can be read but not changed, replaced or deleted, and checks its SHA-256. Keep the
    /// stream open until the process has started: what starts is then the file that was checked. Throws
    /// FileNotFoundException when it isn't there and InvalidOperationException when the hash is wrong.
    internal static FileStream OpenVerified(string path, string sha256)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileNotFoundException($"PresentMon is missing: {path} does not exist, so FPS can't be measured. Reinstalling Marquee puts it back.", path, ex);
        }

        try
        {
            var actual = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"{path} is not the PresentMon Marquee was built with (its SHA-256 is {actual}, not {sha256.ToLowerInvariant()}), so it was not started. Reinstalling Marquee puts the right one back.");
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    static string Metadata(string key) =>
        typeof(PresentMonBundle).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value
            ?? throw new InvalidOperationException($"The build did not set the {key} assembly metadata (see PresentMon.targets).");
}
