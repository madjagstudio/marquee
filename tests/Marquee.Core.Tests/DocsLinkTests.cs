using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Marquee.Core.Tests;

public class DocsLinkTests
{
    static string Repository([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    [Fact]
    public void Theme_guide_exists() =>
        Assert.True(File.Exists(Path.Combine(Repository(), "docs", "themes.md")), "The theme guide docs/themes.md is missing.");

    [Fact]
    public void Public_docs_relative_links_resolve_to_repository_files()
    {
        var root = Repository();
        var files = Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.TopDirectoryOnly)
            .Prepend(Path.Combine(root, "README.md"));
        var problems = new List<string>();
        foreach (var file in files)
        {
            foreach (var destination in Destinations(File.ReadAllText(file)))
            {
                // Web URLs, mail addresses and same-page anchors have no repository file to check.
                if (destination.StartsWith('#') || destination.StartsWith("//", StringComparison.Ordinal)
                    || Regex.IsMatch(destination, @"^[a-zA-Z][a-zA-Z0-9+.-]*:")) continue;
                var path = Uri.UnescapeDataString(destination.Split('#', '?')[0]);
                if (path.Length == 0) continue;
                var target = Path.GetFullPath(path.StartsWith('/')
                    ? Path.Combine(root, path.TrimStart('/'))
                    : Path.Combine(Path.GetDirectoryName(file)!, path));
                var relative = Path.GetRelativePath(root, target).Replace('\\', '/');
                if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                    || !File.Exists(target))
                    problems.Add($"{Path.GetRelativePath(root, file)}: {destination} does not resolve to a file in the repo.");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // Inline links and images, plus reference definitions (including shortcut and collapsed references).
    // Allow angle-bracket destinations, optional titles and balanced parentheses in a bare destination.
    static IEnumerable<string> Destinations(string markdown)
    {
        markdown = Regex.Replace(markdown, @"(?ms)^ {0,3}(?<fence>`{3,}|~{3,})[^\r\n]*\r?\n.*?^ {0,3}\k<fence>\s*$", "");
        markdown = Regex.Replace(markdown, @"(?s)<!--.*?-->", "");
        const string destination = @"(?:<(?<target>[^>\r\n]+)>|(?<target>(?:[^\s()]+|\((?:[^\s()]+|\([^\s()]*\))*\))+))";
        const string title = "(?:\\s+(?:\"[^\"]*\"|'[^']*'|\\([^)]*\\)))?";
        foreach (Match match in Regex.Matches(markdown, @"\]\(\s*" + destination + title + @"\s*\)"))
            yield return match.Groups["target"].Value;
        foreach (Match match in Regex.Matches(markdown, @"(?m)^ {0,3}\[[^\]\r\n]+\]:\s*" + destination))
            yield return match.Groups["target"].Value;
    }

    [Fact]
    public void Link_destinations_include_images_and_references_but_not_fenced_examples()
    {
        const string markdown = """
            [guide](themes.md#start) ![picture](<images/a b.png> "A picture")
            [reference][keys] [keys][] [keys]
            [keys]: keys.md 'Readings'
            [parentheses](images/a(b).png)
            ```md
            [example](missing.md)
            ```
            <!-- [unpublished](missing.md) -->
            """;
        Assert.Equal(["themes.md#start", "images/a b.png", "images/a(b).png", "keys.md"], Destinations(markdown));
    }
}
