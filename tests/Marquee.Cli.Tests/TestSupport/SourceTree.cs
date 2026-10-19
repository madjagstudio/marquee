namespace Marquee.Cli.Tests.TestSupport;

/// The repository checkout the tests were built from, found by walking up from the test assembly to Marquee.sln.
static class SourceTree
{
    public static string Root { get; } = FindRoot();

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(folder.FullName, "Marquee.sln")))
                return folder.FullName;
        }

        throw new InvalidOperationException($"No Marquee.sln above {AppContext.BaseDirectory}.");
    }
}
