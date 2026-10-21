using System.ComponentModel;
using System.Diagnostics;
using Marquee.Core.Processes;
using Marquee.Host;

/// `marquee edit [theme or folder]`: starts the theme editor, MarqueeEditor.exe beside marquee.exe, on a theme found as
/// `marquee run` finds one (ThemeLocator), or with no theme, when the editor opens the one config.yaml names. It doesn't
/// wait for the editor. From an elevated terminal it refuses: the editor would run elevated too (it refuses that
/// itself), and theme files it saved would belong to Administrators.
static class EditCommand
{
    /// What the command needs from the PC, so tests can fake it: whether this terminal is elevated, where themes are
    /// looked for, the editor's exe, and what starts it, which no test lets start anything.
    internal sealed record Machine(Func<bool> IsElevated, ThemeLocator Locator, string EditorExe, Action<ProcessStartInfo> Start)
    {
        public static Machine Real =>
            new(ProcessElevation.IsCurrentProcessElevated, ThemeLocator.Default, ThemeEditor.BesideThisExe, StartProcess);

        // Without the shell, Start returns the process or throws. Only its handle is let go: the editor runs on.
        static void StartProcess(ProcessStartInfo start) => Process.Start(start)?.Dispose();
    }

    /// Null means the arguments were wrong and the caller should print usage.
    public static int? Run(string[] args) => Run(args, Machine.Real);

    internal static int? Run(string[] args, Machine machine)
    {
        if (args is not ([] or [_]) || args is [var option] && option.StartsWith("--", StringComparison.Ordinal)) return null;

        if (machine.IsElevated())
        {
            Console.Error.WriteLine(Strings.EditElevated);
            return 1;
        }

        if (!File.Exists(machine.EditorExe))
        {
            Console.Error.WriteLine(Strings.EditorMissing(machine.EditorExe));
            return 1;
        }

        // The editor starts in its own folder, so a theme is passed as a full path, found from this terminal's folder.
        string? folder = null;
        if (args is [var theme] && !machine.Locator.TryLocate(theme, out folder, out var notFound))
        {
            Console.Error.WriteLine(notFound);
            return 1;
        }

        try
        {
            machine.Start(StartInfo(machine.EditorExe, folder));
        }
        catch (Win32Exception ex)
        {
            Console.Error.WriteLine(Strings.EditorNotStarted(ex.Message));
            return 1;
        }

        Console.WriteLine(folder is null ? Strings.EditStartedConfigured : Strings.EditStarted(folder));
        return 0;
    }

    /// The editor started directly, not through the shell, so nothing but this exe can run, with the theme as one
    /// argument (ArgumentList quotes it), from the install folder rather than the terminal's.
    static ProcessStartInfo StartInfo(string editorExe, string? folder)
    {
        var start = new ProcessStartInfo(editorExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(editorExe)!,
        };
        if (folder is not null) start.ArgumentList.Add(folder);
        return start;
    }
}
