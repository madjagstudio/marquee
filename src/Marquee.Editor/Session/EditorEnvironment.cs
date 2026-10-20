using Marquee.Core;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Startup;
using Marquee.Host;

namespace Marquee.Editor.Session;

/// Everything the shell reaches outside itself, given in one place so tests point it at temp folders, a fake clock,
/// fake value sources and fake dialogs, and never at %LOCALAPPDATA%\Marquee, the real config.yaml, the tray's pipes
/// or the PC's sensors.
public sealed record EditorEnvironment
{
    /// The user's themes: %LOCALAPPDATA%\Marquee\themes, where copies and new themes go.
    public required string UserThemes { get; init; }

    /// The examples the installer puts beside the exe, which the editor never saves (ThemeOpening).
    public required string InstalledThemes { get; init; }

    /// config.yaml, which "Show on screen" writes and an editor started with no arguments reads.
    public required string ConfigFile { get; init; }

    /// Where the editor keeps the opened versions and recovery copies.
    public required EditorFolders Folders { get; init; }

    /// Finds a theme named on the command line: the theme folders, then the current folder.
    public required ThemeLocator Locator { get; init; }

    /// Makes the value sources for each theme opened, on the UI thread.
    public required Func<ValueSources> ValueSources { get; init; }

    public required IEditorDialogs Dialogs { get; init; }

    public required IClipboardText Clipboard { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// Whether this process runs elevated, which the editor refuses (Elevation).
    public Func<bool> IsElevated { get; init; } = Elevation.IsCurrentProcessElevated;

    public ThemeOpening Opening => new(InstalledThemes, UserThemes);

    /// The real places, sources and dialogs. The editor's own readings follow config.yaml's `sensors:` and `providers:`
    /// switches, read from the same config.yaml "Show on screen" writes.
    public static EditorEnvironment ForThisUser()
    {
        var configFile = MarqueePaths.ConfigFile;
        return new EditorEnvironment
        {
            UserThemes = MarqueePaths.Themes,
            InstalledThemes = Path.Combine(AppContext.BaseDirectory, "themes"),
            ConfigFile = configFile,
            Folders = EditorFolders.Default,
            Locator = ThemeLocator.Default,
            ValueSources = () => Model.Values.ValueSources.ForThisUser(configFile),
            Dialogs = new MessageBoxDialogs(),
            Clipboard = new WpfClipboard(),
        };
    }
}
