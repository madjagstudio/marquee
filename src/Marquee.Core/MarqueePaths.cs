using System.Security.Principal;
using Marquee.Core.Files;
using Marquee.Core.Processes;

namespace Marquee.Core;

/// Where Marquee keeps its own files, all of it per user, which the uninstaller leaves. What the user edits (config.yaml,
/// their themes) is under %LOCALAPPDATA%\Marquee. Marquee's own files (the log, the Claude limits) are there too for an
/// unelevated Marquee, but an elevated one keeps them under %ProgramData%\Marquee\users\<SID>: the user's unelevated
/// programs control %LOCALAPPDATA%, and a junction or link they plant there could redirect an elevated write anywhere.
public static class MarqueePaths
{
    static readonly Lazy<string> UserSid = new(() =>
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    });

    static readonly Lazy<string> ThisProcessData = new(() =>
        PrepareDataFolder(Current, ProcessElevation.IsCurrentProcessElevated(), path => SecureFolder.Ensure(path)));

    static MarqueeFolders Current => For(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        UserSid.Value);

    /// %LOCALAPPDATA%\Marquee.
    public static string DataRoot => Current.DataRoot;

    /// %ProgramData%\Marquee\users\<the current user's SID>.
    public static string ElevatedDataRoot => Current.ElevatedDataRoot;

    /// %ProgramData%\Marquee\updates, which the tray makes admin-only (SecureFolder.Ensure) before downloading into it.
    public static string Updates => Current.Updates;

    /// The unelevated log folder, %LOCALAPPDATA%\Marquee\logs. A process logs to ProcessLogs, which is this one only
    /// when it isn't elevated.
    public static string Logs => Current.LogsFolder(elevated: false);

    public static string Themes => Current.Themes;

    public static string ConfigFile => Current.ConfigFile;

    public static string DataFolder(bool elevated) => Current.DataFolder(elevated);

    public static string LogsFolder(bool elevated) => Current.LogsFolder(elevated);

    /// The folders under other roots, for tests.
    public static MarqueeFolders For(string localAppData, string programData, string userSid) =>
        new(localAppData, programData, userSid);

    /// The folder this process keeps its log and the Claude limits in, chosen once: DataFolder(elevated) for this
    /// process, and for an elevated one made admin-only first. When that folder can't be made safe it throws
    /// UnsafePathException (or the IOException that stopped it), every time it is asked: an elevated Marquee never
    /// falls back to %LOCALAPPDATA%. Ask before starting anything that writes there, so the error can be shown.
    public static string ProcessDataFolder => ThisProcessData.Value;

    /// The log folder in ProcessDataFolder.
    public static string ProcessLogs => Path.Combine(ProcessDataFolder, "logs");

    internal static string PrepareDataFolder(MarqueeFolders folders, bool elevated, Action<string> secure)
    {
        var folder = folders.DataFolder(elevated);
        if (elevated) secure(folder);
        return folder;
    }
}
