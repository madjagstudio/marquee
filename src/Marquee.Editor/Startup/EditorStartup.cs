using Marquee.Editor.Resources;
using Marquee.Editor.Session;

namespace Marquee.Editor.Startup;

/// What the editor does before it shows a window: refuses to run elevated, works out the theme from its arguments,
/// and opens it (EditorWorkspace.Open). Kept out of App, so tests run it with a fake environment.
public static class EditorStartup
{
    /// The workspace to show; null when the editor should exit, after saying why where there is something to say.
    public static EditorWorkspace? Open(IReadOnlyList<string> args, EditorEnvironment environment, SynchronizationContext ui)
    {
        if (environment.IsElevated())
        {
            environment.Dialogs.ShowError(Strings.RefuseElevated);
            return null;
        }

        var target = EditorArguments.Resolve(args, environment.Locator, environment.ConfigFile);
        if (target.Folder is null)
        {
            environment.Dialogs.ShowError(Strings.ThemeNotFound(target.Error!));
            return null;
        }

        return EditorWorkspace.Open(target.Folder, environment, ui);
    }
}
