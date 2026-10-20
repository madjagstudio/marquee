namespace Marquee.Editor.Tests.TestSupport;

/// An empty folder under %TEMP%, deleted on dispose. The shell's tests keep everything in one: the user's themes, the
/// installed examples, config.yaml and the editor's own folder, in place of %LOCALAPPDATA%\Marquee and Program Files.
sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marquee-shell-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // %TEMP% is cleaned eventually.
        }
    }
}
