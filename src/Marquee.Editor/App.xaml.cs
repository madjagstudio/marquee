using System.Windows;
using Marquee.Editor.Session;
using Marquee.Editor.Shell;
using Marquee.Editor.Startup;

namespace Marquee.Editor;

/// MarqueeEditor.exe. It refuses to run elevated, finds the theme from its arguments or config.yaml, opens it
/// (EditorStartup), and shows the window; when there is nothing to show (refused, declined, or the theme is open in
/// another editor, which comes forward instead) it exits.
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var environment = EditorEnvironment.ForThisUser();
        var ui = SynchronizationContext.Current ?? throw new InvalidOperationException("WPF starts with a UI context.");
        if (EditorStartup.Open(e.Args, environment, ui) is not { } workspace)
        {
            Shutdown(1);
            return;
        }

        var window = new MainWindow(new MainViewModel(environment, workspace, ui));
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }
}
