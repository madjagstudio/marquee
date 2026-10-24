using System.Runtime.CompilerServices;
using Marquee.Editor.Model.Values;

namespace Marquee.Editor.Model.Tests.Values;

public class KeysDocTests
{
    [Fact]
    public void Reference_documents_every_named_sample_key_and_only_known_keys()
    {
        var path = ReferencePath();
        Assert.True(File.Exists(path), "docs/keys.md must document the sensor keys.");

        // Every table has its key in the first column. Read all data rows, including patterns,
        // without filtering by known key names: a typo must fail rather than disappear.
        var keys = File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Split('|')[1].Trim())
            .Where(cell => !cell.Equals("Key", StringComparison.OrdinalIgnoreCase)
                && !cell.All(c => c is '-' or ':' or ' '))
            .Select(cell => cell.Trim('`'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(keys);
        var unknown = keys.Where(key => !Samples.Knows(key)).Order().ToArray();
        Assert.True(unknown.Length == 0, $"Unknown documented keys: {string.Join(", ", unknown)}");
        var missing = Samples.Keys.Where(key => !key.Contains('*') && !keys.Contains(key)).Order().ToArray();
        Assert.True(missing.Length == 0, $"Keys missing from docs/keys.md: {string.Join(", ", missing)}");
    }

    // Locate the source checkout even when build outputs are redirected to a temporary folder.
    static string ReferencePath([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "..", "docs", "keys.md"));
}
