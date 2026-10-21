namespace Marquee.Host;

/// The theme editor, MarqueeEditor.exe, which the installer puts beside marquee.exe and MarqueeTray.exe. The tray's
/// "Edit theme…" and `marquee edit` start it from there.
public static class ThemeEditor
{
    public const string ExeName = "MarqueeEditor.exe";

    /// Where it is for a Marquee exe in the install folder: beside that exe. A development build has it in its own
    /// project's output folder instead, so there it is missing.
    public static string BesideThisExe => Path.Combine(AppContext.BaseDirectory, ExeName);
}
