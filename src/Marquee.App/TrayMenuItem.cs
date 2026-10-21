namespace Marquee.App;

/// One line of the tray menu, as data: TrayModel builds the menu from these and TrayApp renders them, so the menu's
/// contents and what each item does are tested without WinForms. `Key` names the item for tests and for code that
/// adds items next to another (MenuKeys).
sealed record TrayMenuItem(string Key, string Text)
{
    public bool Enabled { get; init; } = true;

    public bool Checked { get; init; }

    /// What clicking it does, on the UI thread. Null for a line that only says something (the status) or opens a
    /// submenu.
    public Action? Command { get; init; }

    /// A submenu's items; empty for a plain item.
    public IReadOnlyList<TrayMenuItem> Children { get; init; } = [];

    public bool IsSeparator => Key == MenuKeys.Separator;

    public static TrayMenuItem Separator { get; } = new(MenuKeys.Separator, "");
}

/// The keys of the tray menu's items.
static class MenuKeys
{
    public const string Separator = "-";
    public const string Status = "status";
    public const string Problem = "problem";
    public const string Themes = "themes";
    public const string Theme = "theme";
    public const string NoThemes = "no-themes";
    public const string Orientation = "orientation";
    public const string Preview = "preview";
    public const string EditTheme = "edit-theme";
    public const string ThemesFolder = "themes-folder";
    public const string Logs = "logs";
    public const string Settings = "settings";
    public const string TakeOver = "take-over";
    public const string AlwaysTakeOver = "always-take-over";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string CheckForUpdates = "check-for-updates";
    public const string InstallUpdate = "install-update";
    public const string About = "about";
    public const string Quit = "quit";
}
