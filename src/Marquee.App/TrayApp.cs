using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Marquee.App.Resources;
using Marquee.Core.Logging;
using Microsoft.Win32;

namespace Marquee.App;

/// The tray icon and its menu: the WinForms side of TrayModel. It draws the model's items into a ContextMenuStrip each
/// time the menu opens, so the status line is current, forwards clicks to the items' commands, and does what the
/// model asks of a window (ITrayHost) on the UI thread. Everything else, the engine included, is the model's and the
/// runtime's; nothing here calls the engine.
sealed class TrayApp : ApplicationContext, ITrayHost
{
    const uint MSGFLT_ADD = 1;
    static readonly TimeSpan BalloonTimeout = TimeSpan.FromSeconds(10);

    /// NotifyIcon opens its menu itself only on a right click, through this private method, which also makes the menu
    /// close when the user clicks elsewhere (it brings the icon's window to the front first). It has been there since
    /// .NET Framework; if a later WinForms drops it, the menu still opens, at the pointer, from Show.
    static readonly MethodInfo? ShowContextMenu =
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes);

    readonly TrayRuntime _runtime;
    readonly TrayModel _model;
    readonly WindowsFormsSynchronizationContext _ui;
    readonly ContextMenuStrip _menu = new();
    readonly NotifyIcon _icon;
    readonly DeviceChangeWindow _devices;
    readonly System.Windows.Forms.Timer _tooltip = new() { Interval = 2000 };
    PreviewSession? _preview;

    /// Call on the UI thread, before Application.Run.
    public TrayApp(TrayRuntime runtime, TrayPaths paths, IShell shell, EngineWorker worker, ILog log, SingleInstance instance)
    {
        _runtime = runtime;
        _ui = new WindowsFormsSynchronizationContext();
        _model = new TrayModel(runtime, paths, shell, this, worker, log);

        // Explorer broadcasts TaskbarCreated when it starts again (after a crash, or a restart from Task Manager), and
        // NotifyIcon adds its icon again when it hears it. But Windows keeps messages from a lower integrity level away
        // from an elevated process's windows, so the elevated tray must let this one through, or its icon would be gone
        // until the next logon.
        ChangeWindowMessageFilter(RegisterWindowMessage("TaskbarCreated"), MSGFLT_ADD);

        _menu.Opening += (_, e) =>
        {
            Render(_menu.Items, _model.BuildMenu());
            e.Cancel = false; // the strip cancels opening when it had no items before this handler filled it
        };
        _icon = new NotifyIcon
        {
            Icon = TrayIcons.Load(SystemInformation.SmallIconSize),
            Text = Strings.AppName,
            ContextMenuStrip = _menu,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) PopMenu();
        };
        _icon.BalloonTipClicked += (_, _) => PopMenu();
        _icon.Visible = true;

        _devices = new DeviceChangeWindow(runtime.RetrySoon);
        _tooltip.Tick += (_, _) => _icon.Text = _model.Tooltip;
        _tooltip.Start();
        instance.OnShowRequested(() => _ui.Post(_ => PopMenu(), null));
        // At logoff or shutdown Windows ends the process soon after; stopping the engine first lets go of the screen
        // and saves the Claude limits. The runtime is thread-safe, and this is SystemEvents' own thread.
        SystemEvents.SessionEnded += OnSessionEnded;

        _ = _model.Start();
    }

    public void ShowBalloon(string title, string text, bool isError) =>
        _ui.Post(_ => _icon.ShowBalloonTip((int)BalloonTimeout.TotalMilliseconds, title, text, isError ? ToolTipIcon.Error : ToolTipIcon.Info), null);

    public void ShowMessage(string title, string text) =>
        _ui.Post(_ => MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information), null);

    public void OpenPreview() => _ui.Post(_ =>
    {
        if (_preview is not null) return;
        _preview = PreviewSession.Open(_runtime);
        if (_preview is { } preview) preview.Closed += () => _ui.Post(_ => { if (_preview == preview) _preview = null; }, null);
    }, null);

    public void Exit() => _ui.Post(_ => ExitThread(), null);

    protected override void ExitThreadCore()
    {
        SystemEvents.SessionEnded -= OnSessionEnded;
        _tooltip.Dispose();
        _preview?.Dispose();
        _icon.Visible = false; // gone at once, not when the pointer next passes over it
        _icon.Dispose();
        _devices.Dispose();
        _menu.Dispose();
        base.ExitThreadCore();
    }

    void OnSessionEnded(object? sender, SessionEndedEventArgs e) => _runtime.Dispose();

    void PopMenu()
    {
        if (ShowContextMenu is not null) ShowContextMenu.Invoke(_icon, null);
        else _menu.Show(Cursor.Position);
    }

    /// Replaces `target`'s items with ToolStrip items for `items`.
    static void Render(ToolStripItemCollection target, IReadOnlyList<TrayMenuItem> items)
    {
        // Disposing an item takes it out of the menu too; Clear alone would leak their handles at every opening.
        foreach (var old in target.Cast<ToolStripItem>().ToList()) old.Dispose();
        foreach (var item in items) target.Add(ToolStripItemFor(item));
    }

    static ToolStripItem ToolStripItemFor(TrayMenuItem item)
    {
        if (item.IsSeparator) return new ToolStripSeparator();

        // "&" marks a keyboard shortcut in a menu; a theme or an error may hold one, which should show as itself.
        var menuItem = new ToolStripMenuItem(item.Text.Replace("&", "&&")) { Enabled = item.Enabled, Checked = item.Checked };
        if (item.Command is { } command) menuItem.Click += (_, _) => command();
        foreach (var child in item.Children) menuItem.DropDownItems.Add(ToolStripItemFor(child));
        return menuItem;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool ChangeWindowMessageFilter(uint message, uint flag);
}
