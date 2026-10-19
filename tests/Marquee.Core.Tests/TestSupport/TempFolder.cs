namespace Marquee.Core.Tests.TestSupport;

/// An empty folder under %TEMP%, deleted on dispose.
sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marquee-" + Guid.NewGuid().ToString("N")[..8]);

    public TempFolder() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A decoder that a failing test didn't dispose may still hold a clip open; %TEMP% is cleaned eventually.
        }
    }
}
