using System.Text;

namespace Marquee.Providers.Agents;

/// Writes that leave either the old file or the new one, never half of each: write a temporary file beside the
/// target, then swap it in.
static class AtomicFile
{
    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".marquee-tmp";
        File.WriteAllText(temporary, text, Utf8);
        File.Move(temporary, path, overwrite: true);
    }

    /// Replaces an existing file, keeping its previous contents at `backup`.
    public static void Replace(string path, string text, string backup)
    {
        var temporary = path + ".marquee-tmp";
        File.WriteAllText(temporary, text, Utf8);
        File.Replace(temporary, path, backup);
    }
}
