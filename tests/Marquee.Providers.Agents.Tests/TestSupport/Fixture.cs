namespace Marquee.Providers.Agents.Tests.TestSupport;

/// Files under Fixtures/, copied next to the test assembly. All of them are synthesized: no real session data.
static class Fixture
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Text(string name) => File.ReadAllText(Path(name));

    public static byte[] Bytes(string name) => File.ReadAllBytes(Path(name));
}
