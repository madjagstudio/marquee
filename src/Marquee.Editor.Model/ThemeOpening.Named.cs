using System.Globalization;

namespace Marquee.Editor.Model;

/// "New from an example": a copy under a name the user gives, which becomes the theme's folder name, and so its name in
/// the tray's Themes menu and in config.yaml's `theme:`.
public sealed partial class ThemeOpening
{
    /// The longest name a copy may have, in characters: room for the path under %LOCALAPPDATA% and for the name in a
    /// menu.
    public const int MaxNameLength = 64;

    /// The device names Windows keeps for itself, with or without an extension.
    static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// Why `name`, trimmed, can't name a copy in the user's themes, in a sentence for the user; null when it can. The
    /// name must be a folder name Windows takes and the tray lists (not hidden, not a device name), and no theme there
    /// may have it yet, in any letter case.
    public string? CopyNameProblem(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return "Give the theme a name.";
        if (trimmed.Length > MaxNameLength)
            return string.Create(CultureInfo.InvariantCulture, $"A theme's name can have at most {MaxNameLength} characters.");
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "A theme's name can't have any of these: \\ / : * ? \" < > |";
        // The tray and the clean-up pass over dot names as hidden, and the copy is staged under one.
        if (trimmed.StartsWith('.')) return "A theme's name can't start with a dot.";
        // Windows drops a dot at the end of a folder name, so the folder would have another name than the one given.
        if (trimmed.EndsWith('.')) return "A theme's name can't end with a dot.";
        var stem = trimmed.Split('.')[0].TrimEnd();
        if (Reserved.Contains(stem, StringComparer.OrdinalIgnoreCase))
            return $"Windows keeps the name {stem} for a device; choose another.";
        var target = Path.Combine(_userThemes, trimmed);
        if (Directory.Exists(target) || File.Exists(target)) return $"There is already a theme named {trimmed}.";
        return null;
    }

    /// Copies the theme in `themeFolder`, as MakeCopy does, into a folder named `name` (trimmed) in the user's themes,
    /// and returns that folder. Throws ArgumentException with CopyNameProblem's reason for a name it refuses, before
    /// anything is copied.
    public string MakeCopy(string themeFolder, string name)
    {
        if (CopyNameProblem(name) is { } problem) throw new ArgumentException(problem, nameof(name));
        return CopyAs(ThemeIdentity.FullPath(themeFolder), Path.Combine(_userThemes, name.Trim()));
    }
}
