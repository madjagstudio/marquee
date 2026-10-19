using System.Globalization;
using System.IO.Compression;

namespace Marquee.Core.Themes.Validation;

/// A zipped theme, extracted into a new folder under %TEMP% that Dispose deletes. A zip comes from someone else, so
/// every entry is checked before anything is written, and nothing about it is taken on trust: its name must stay inside
/// the folder (checked here, not left to .NET's extraction), it must not be a link, it must not expand more than a real
/// file would, and the sizes the zip declares must fit the theme limits. Those sizes are then held to as each entry is
/// written: an entry that turns out longer than declared, or doesn't match its checksum, stops the extraction.
///
/// The theme is either at the top of the zip (layout.yaml at the top level) or in its one top-level folder. Entries
/// under __MACOSX, the resource forks macOS's Compress adds next to that folder, are checked but not extracted.
sealed class ThemeZip : IDisposable
{
    const string MacMetadataFolder = "__MACOSX";

    /// Names Windows reserves for devices, with or without an extension: a file can't be called nul.png.
    static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    /// An entry that passed the checks: its path split into folder names, and whether it is a folder entry.
    sealed record Entry(ZipArchiveEntry Source, string[] Parts, bool IsFolder);

    readonly string _root = Path.Combine(Path.GetTempPath(), "marquee-validate-" + Guid.NewGuid().ToString("N")[..8]);

    ThemeZip() { }

    /// The extracted theme folder, or null when the zip was refused (the issues say why).
    public string? ThemeFolder { get; private set; }

    /// The name of the zip's top-level folder the theme is in, or null when it is at the top of the zip.
    public string? ThemeName { get; private set; }

    /// Never throws for a problem with the zip; each one becomes an issue about the theme as a whole.
    public static ThemeZip Extract(string zipPath, ThemeLimits limits, List<ThemeIssue> issues)
    {
        var zip = new ThemeZip();
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            zip.ExtractFrom(archive, limits, issues);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            issues.Add(ThemeIssue.Error("", $"The zip file could not be read: {ex.Message}"));
        }

        return zip;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Everything validation opened is closed by now, so this is something else holding a file (an antivirus
            // scan, say); %TEMP% is cleaned eventually.
        }
    }

    void ExtractFrom(ZipArchive archive, ThemeLimits limits, List<ThemeIssue> issues)
    {
        var all = archive.Entries;
        if (all.Count > limits.MaxFiles)
        {
            issues.Add(ThemeIssue.Error("", string.Create(CultureInfo.InvariantCulture,
                $"The zip has {all.Count:N0} entries, more than the {limits.MaxFiles:N0} files a theme may have.")));
            return;
        }

        var entries = new List<Entry>();
        var total = 0L;
        foreach (var source in all)
        {
            if (Problem(source, limits, out var parts) is { } problem)
            {
                issues.Add(ThemeIssue.Error("", problem));
                continue;
            }

            total += source.Length;
            entries.Add(new Entry(source, parts, source.FullName.EndsWith('/') || source.FullName.EndsWith('\\')));
        }

        if (total > limits.MaxTotalBytes)
        {
            issues.Add(ThemeIssue.Error("", $"The zip's files add up to {ThemeFiles.Megabytes(total)} MB uncompressed, " +
                $"over the {ThemeFiles.Megabytes(limits.MaxTotalBytes)} MB limit for a theme."));
        }

        if (entries.Count < all.Count || total > limits.MaxTotalBytes) return; // refused: nothing is written

        entries.RemoveAll(e => string.Equals(e.Parts[0], MacMetadataFolder, StringComparison.OrdinalIgnoreCase));
        if (ThemeRoot(entries, issues) is not { } root) return;

        Directory.CreateDirectory(_root);
        foreach (var entry in entries)
            if (!Write(entry, issues)) return;

        ThemeFolder = root.Length == 0 ? _root : Path.Combine(_root, root);
        ThemeName = root.Length == 0 ? null : root;
    }

    /// Why the entry can't be extracted, or null with its path split into names.
    static string? Problem(ZipArchiveEntry entry, ThemeLimits limits, out string[] parts)
    {
        var name = entry.FullName;
        parts = [];

        // A zip made on Unix keeps each entry's mode in the top 16 bits; S_IFLNK marks a symbolic link, whose data is
        // the path it points to. Zips made on Windows leave those bits 0.
        if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            return $"Entry '{name}' is a symbolic link, which a shared theme can't contain.";

        if (name.StartsWith('/') || name.StartsWith('\\') || (name.Length >= 2 && name[1] == ':'))
            return $"Entry '{name}' has an absolute path; every file must be inside the theme.";

        // Zips should use '/', but some Windows tools write '\', which Windows would read as a folder too.
        parts = name.Split('/', '\\').Where(part => part.Length > 0 && part != ".").ToArray();
        if (parts.Contains(".."))
            return $"Entry '{name}' uses '..' to reach outside the zip.";
        if (parts.Length == 0 || !parts.All(IsValidName))
            return $"Entry '{name}' is not a valid file name on Windows.";

        if (entry.Length > (long)limits.MaxCompressionRatio * Math.Max(entry.CompressedLength, 1))
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"Entry '{name}' would expand more than {limits.MaxCompressionRatio} times, from {entry.CompressedLength:N0} " +
                $"bytes to {entry.Length:N0}; the zip is refused as a possible zip bomb.");
        }

        return null;
    }

    /// Windows drops a trailing dot or space, so "a." would land on "a"; device names open the device, not a file.
    static bool IsValidName(string name) =>
        name.IndexOfAny(InvalidNameChars) < 0 &&
        !name.EndsWith('.') && !name.EndsWith(' ') &&
        !DeviceNames.Contains(name.Split('.')[0]);

    /// "" when the theme is at the top of the zip, the folder's name when it is in the zip's only top-level folder, or
    /// null (with an issue saying where layout.yaml should be) otherwise.
    static string? ThemeRoot(List<Entry> entries, List<ThemeIssue> issues)
    {
        if (entries.Count == 0)
        {
            issues.Add(ThemeIssue.Error("", $"The zip is empty. {ThemeLoader.LayoutFileName} should be at its top level or in its one top-level folder."));
            return null;
        }

        if (entries.Any(e => !e.IsFolder && e.Parts is [var file] && IsLayout(file)))
            return "";

        var tops = entries.Select(e => e.Parts[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tops.Count != 1 || entries.Any(e => !e.IsFolder && e.Parts.Length == 1))
        {
            issues.Add(ThemeIssue.Error("", $"There is no {ThemeLoader.LayoutFileName} at the top of the zip, and its files aren't all in " +
                $"one folder. Put {ThemeLoader.LayoutFileName} at the top level, or the whole theme in one folder."));
            return null;
        }

        var folder = tops[0];
        if (!entries.Any(e => !e.IsFolder && e.Parts is [_, var file] && IsLayout(file)))
        {
            issues.Add(ThemeIssue.Error("", $"There is no {ThemeLoader.LayoutFileName} in the zip's folder '{folder}'; it should be at " +
                $"{folder}/{ThemeLoader.LayoutFileName}."));
            return null;
        }

        return folder;
    }

    static bool IsLayout(string name) => string.Equals(name, ThemeLoader.LayoutFileName, StringComparison.OrdinalIgnoreCase);

    /// Writes one entry under the temporary folder; false, with an issue, if it must stop the extraction.
    bool Write(Entry entry, List<ThemeIssue> issues)
    {
        var name = entry.Source.FullName;
        var target = Path.GetFullPath(Path.Combine([_root, .. entry.Parts]));

        // The names were checked one by one already; this checks what Windows makes of them together.
        if (!target.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(ThemeIssue.Error("", $"Entry '{name}' is not a valid file name on Windows."));
            return false;
        }

        if (entry.IsFolder)
        {
            Directory.CreateDirectory(target);
            return true;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        FileStream output;
        try
        {
            output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (Path.Exists(target))
        {
            issues.Add(ThemeIssue.Error("", $"Entry '{name}' appears in the zip more than once."));
            return false;
        }

        bool intact;
        using (output)
        {
            try
            {
                using var input = entry.Source.Open();
                intact = Copy(input, output, entry.Source.Length, entry.Source.Crc32);
            }
            catch (InvalidDataException)
            {
                intact = false; // compressed data that doesn't decompress
            }
        }

        if (!intact)
            issues.Add(ThemeIssue.Error("", $"Entry '{name}' doesn't match the size or checksum the zip records for it; the zip is damaged."));
        return intact;
    }

    /// Copies an entry's data, stopping as soon as it runs past the declared length, and checks the length and checksum.
    static bool Copy(Stream input, Stream output, long declaredLength, uint declaredCrc)
    {
        var buffer = new byte[81920];
        var written = 0L;
        var crc = 0u;
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            written += read;
            if (written > declaredLength) return false;
            crc = Crc32.Append(crc, buffer.AsSpan(0, read));
            output.Write(buffer, 0, read);
        }

        return written == declaredLength && crc == declaredCrc;
    }
}
