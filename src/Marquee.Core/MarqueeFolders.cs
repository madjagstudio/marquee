namespace Marquee.Core;

/// Marquee's folders under the roots given: the user's %LOCALAPPDATA%, the machine's %ProgramData% and the user's SID.
/// MarqueePaths has the real ones; tests make their own.
public sealed record MarqueeFolders(string LocalAppData, string ProgramData, string UserSid)
{
    /// %LOCALAPPDATA%\Marquee: what the user edits (config.yaml, their themes) and an unelevated Marquee's own files.
    public string DataRoot => Path.Combine(LocalAppData, "Marquee");

    /// %ProgramData%\Marquee\users\<SID>: an elevated Marquee's own files (the log, the Claude limits). Only
    /// administrators can write there and the user can read it (SecureFolder), so no unelevated program can plant a
    /// link that redirects an elevated write.
    public string ElevatedDataRoot => Path.Combine(ProgramData, "Marquee", "users", UserSid);

    /// %ProgramData%\Marquee\updates: where the tray downloads an update, for the machine, not per user. Only
    /// administrators can write there (SecureFolder), so nothing can swap the installer between the check and msiexec.
    public string Updates => Path.Combine(ProgramData, "Marquee", "updates");

    /// The user's own themes, looked up by name before the installed examples.
    public string Themes => Path.Combine(DataRoot, "themes");

    /// config.yaml: the theme, orientation, providers and other settings `marquee run` and the tray start with.
    public string ConfigFile => Path.Combine(DataRoot, "config.yaml");

    /// Where a Marquee that is (or isn't) elevated keeps its log and the Claude limits.
    public string DataFolder(bool elevated) => elevated ? ElevatedDataRoot : DataRoot;

    /// The daily log files FileLog writes, which the tray's "Open logs" opens.
    public string LogsFolder(bool elevated) => Path.Combine(DataFolder(elevated), "logs");
}
