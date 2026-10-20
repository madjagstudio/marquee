namespace Marquee.ReleaseTool.Tests.TestSupport;

/// A folder under %TEMP%, deleted on dispose.
sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marquee-test-" + Guid.NewGuid().ToString("N")[..8]);

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string relative) => System.IO.Path.Combine(Path, relative);

    /// Writes a file and returns its full path.
    public string Write(string relative, string contents)
    {
        var path = File(relative);
        System.IO.File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Something still has a file open; %TEMP% is cleaned eventually.
        }
    }
}
