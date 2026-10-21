namespace Marquee.Editor.Model;

/// One editor per theme folder: a named mutex (ThemeIdentity.MutexName) that exists while an editor has the theme open.
/// Only its existence counts, not who owns it, so it can be let go from any thread; and Windows lets it go when the
/// process ends, however it ends.
public sealed class ThemeLock : IDisposable
{
    readonly Mutex _mutex;

    ThemeLock(Mutex mutex, string name)
    {
        _mutex = mutex;
        Name = name;
    }

    /// The mutex's name, from which the shell can name its "bring the window forward" signal.
    public string Name { get; }

    /// Takes the theme folder for this editor, or null when another editor has it open already, so the shell can bring
    /// that one's window forward instead. A mutex this process may not open (an elevated editor's) counts as held.
    public static ThemeLock? TryTake(string themeFolder)
    {
        var name = ThemeIdentity.MutexName(themeFolder);
        try
        {
            var mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
            if (createdNew) return new ThemeLock(mutex, name);
            mutex.Dispose();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _mutex.Dispose();
}
