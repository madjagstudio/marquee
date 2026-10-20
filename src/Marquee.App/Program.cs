using System.Windows.Forms;
using Marquee.App.Resources;
using Marquee.Core.Logging;
using Marquee.Host;
using Marquee.Host.Updates;

namespace Marquee.App;

/// MarqueeTray.exe: the tray icon that runs the engine, one per user. The logon task starts it elevated at every logon;
/// starting it again (from the Start menu, say) opens the running one's menu instead. It has no console and no command
/// line: it logs to the daily log files, and its settings are config.yaml's.
static class Program
{
    [STAThread]
    static int Main()
    {
        ApplicationConfiguration.Initialize();

        using var instance = SingleInstance.TryClaim(TrayNames.Instance, TrayNames.ShowMenu);
        if (instance is null)
        {
            SingleInstance.AskFirstToShow(TrayNames.ShowMenu);
            return 0;
        }

        // Elevated, the log and the Claude limits go in an admin-only folder under %ProgramData%; one that can't be made
        // safe stops the tray here, with the reason, before anything writes there.
        TrayPaths paths;
        try
        {
            paths = TrayPaths.Current();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Strings.DataFolderUnusable(ex.Message), Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        using var log = new FileLog(paths.Logs);
        log.Info(Strings.TrayStarted(TrayModel.Version));
        // Made as soon as there is a log to say why it can't be, and kept until the tray exits, so the installer finds it
        // as long as there is a tray to quit.
        using var quit = QuitEvent.Create(TrayNames.Quit, log);
        // A menu command that throws on the UI thread is logged and the tray carries on, rather than WinForms showing
        // its exception dialog from an elevated process.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => log.Error(Strings.CommandFailed(e.Exception.Message));

        // Disposed in the reverse order: the worker finishes what was queued, then the engine stops (Quit has stopped
        // it already; this covers the tray ending any other way), then the updates let go of an installer they held
        // for msiexec, then the update client and the log close. One HttpClient serves every check and download.
        using var http = UpdateHttp.CreateClient();
        using var updates = TrayUpdates.ForThisApp(http, paths.DataFolder, log);
        using var runtime = new TrayRuntime(paths, log, MachineServices.Instance, TrayNames.Screen);
        using var worker = new EngineWorker();
        Application.Run(new TrayApp(runtime, paths, new ExplorerShell(), worker, updates, log, instance, quit));
        log.Info(Strings.TrayStopped);
        return 0;
    }
}
