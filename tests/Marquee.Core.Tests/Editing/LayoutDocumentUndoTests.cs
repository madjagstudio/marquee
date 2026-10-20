using System.Runtime.CompilerServices;
using System.Text;
using Marquee.Core.Themes.Editing;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Tests.Editing;

/// Undo, as the editor does it: each step keeps the text before and after, and undoing opens the text before. A random
/// run of 200 edits of every kind over each example theme (with LF line breaks, and with CRLF and a byte order mark),
/// replayed backwards, comes back to the file's identical bytes. Each text on the way opens to itself, and no edit
/// changes the document it was made on. The edits are random, from a fixed seed so a failure repeats; an edit the
/// document refuses is left out, as the editor leaves it out of its undo steps.
public class LayoutDocumentUndoTests
{
    static readonly string[] Names = ["example-agents", "example-landscape", "example-portrait", "example-showcase"];

    const int Steps = 200;

    static readonly string[] Values = ["0", "12.5", "text", "two words", "#FF5A36", "{cpu.load:0}%", "a: b", "it's", "", "line\nbreak"];

    static readonly string[] Fragments =
        ["7", "{ size: 3 }", "[1, 2]", "size: 30\nweight: bold", "- a\n- b", "\"#FFFFFF\"", "|\n  one\n  two\n"];

    static readonly string[] Items =
    [
        "{ type: text, at: [0, 0], size: [10, 10], text: A }",
        "type: bar\nat: [0, 0]\nsize: [10, 10]\nsource: cpu.load\nwhen:\n  - { above: 9, style: { fill: \"#FF0000\" } }",
        "5",
        "{ source: cpu.load }",
    ];

    const string Header = "type: group\nat: [0, 0]\nsize: [100, 100]";

    public static TheoryData<string, bool> Runs => new()
    {
        { "example-agents", false }, { "example-agents", true },
        { "example-landscape", false }, { "example-landscape", true },
        { "example-portrait", false }, { "example-portrait", true },
        { "example-showcase", false }, { "example-showcase", true },
    };

    [Theory]
    [MemberData(nameof(Runs))]
    public void Replaying_200_edits_backwards_restores_the_original_bytes(string name, bool crlfWithBom)
    {
        var original = Fixture(name);
        if (crlfWithBom) original = "\uFEFF" + Edits.Crlf(original);
        var bytes = Encoding.UTF8.GetBytes(original);
        var random = new Random(Array.IndexOf(Names, name) * 2 + (crlfWithBom ? 1 : 0));

        var documents = new List<LayoutDocument> { LayoutDocument.Open(Encoding.UTF8.GetString(bytes)) };
        var texts = new List<string> { documents[0].Text };
        var made = new Dictionary<string, int>();
        for (var attempt = 0; documents.Count <= Steps; attempt++)
        {
            Assert.True(attempt < Steps * 20, $"Only {documents.Count - 1} of {Steps} edits were made");
            var (kind, edit) = RandomEdit(documents[^1], random);
            if (edit is null) continue;
            try
            {
                var next = edit(documents[^1]);
                if (next.Text == documents[^1].Text) continue;
                documents.Add(next);
                texts.Add(next.Text);
                made[kind] = made.GetValueOrDefault(kind) + 1;
            }
            catch (LayoutEditException)
            {
                // Refused: the document is unchanged, and the editor makes no undo step.
            }
        }

        Assert.All(Kinds, kind => Assert.True(made.GetValueOrDefault(kind) > 0, $"No {kind} edit was made"));
        for (var i = 0; i < documents.Count; i++) Assert.Equal(texts[i], documents[i].Text);

        var current = documents[^1];
        for (var i = texts.Count - 2; i >= 0; i--)
        {
            current = LayoutDocument.Open(texts[i]);
            Assert.Equal(texts[i], current.Text);
        }

        Assert.Equal(bytes, Encoding.UTF8.GetBytes(current.Text));
    }

    static readonly string[] Kinds = ["set", "replace", "add", "remove", "insert", "remove-item", "move", "wrap", "unwrap"];

    /// A random edit of one of the kinds, on a node the document has, or null when it has none for that kind.
    static (string Kind, Func<LayoutDocument, LayoutDocument>? Edit) RandomEdit(LayoutDocument document, Random random)
    {
        var nodes = Nodes(document.Text.TrimStart('\uFEFF'));
        var kind = Kinds[random.Next(Kinds.Length)];
        T? Pick<T>(IReadOnlyList<T> from) where T : class => from.Count == 0 ? null : from[random.Next(from.Count)];

        switch (kind)
        {
            case "set" when Pick(nodes.Scalars) is { } path:
                var value = Values[random.Next(Values.Length)];
                return (kind, d => d.SetScalar(path, value));
            case "replace" when Pick(nodes.Keys) is { } path:
                var fragment = Fragments[random.Next(Fragments.Length)];
                return (kind, d => d.ReplaceValue(path, fragment));
            case "add" when Pick(nodes.Mappings) is { } path:
                var key = $"k{random.Next(1000)}";
                return (kind, d => d.AddKey(path, key, "1"));
            case "remove" when Pick(nodes.Keys) is { } path:
                return (kind, d => d.RemoveKey(path));
            case "insert" when Pick(nodes.Lists) is { } list:
                var item = Items[random.Next(Items.Length)];
                var index = random.Next(nodes.Count[list] + 1);
                return (kind, d => d.InsertItem(list, index, item));
            case "remove-item" when Pick(nodes.Items) is { } path:
                return (kind, d => d.RemoveItem(path));
            case "move" when Pick(nodes.Items) is { } path && Pick(nodes.Lists.Where(l => !Under(l, path)).ToList()) is { } to:
                var at = random.Next(nodes.Count[to] + (to == path.Parent ? 0 : 1));
                return (kind, d => d.MoveItem(path, to, at));
            case "wrap" when Pick(nodes.Lists.Where(l => nodes.Count[l] > 0).ToList()) is { } list:
                var chosen = Enumerable.Range(0, nodes.Count[list]).Where(_ => random.Next(3) == 0).DefaultIfEmpty(0).ToList();
                return (kind, d => d.WrapInGroup(list, chosen, Header));
            case "unwrap" when Pick(nodes.Groups) is { } path:
                return (kind, d => d.UnwrapGroup(path));
            default:
                return (kind, null);
        }
    }

    static bool Under(YamlPath path, YamlPath above) =>
        path.Segments.Count >= above.Segments.Count && path.Segments.Take(above.Segments.Count).SequenceEqual(above.Segments);

    /// The paths of a file's nodes, by what an edit can do with them.
    sealed record NodePaths(List<YamlPath> Scalars, List<YamlPath> Keys, List<YamlPath> Mappings, List<YamlPath> Lists,
        List<YamlPath> Items, List<YamlPath> Groups, Dictionary<YamlPath, int> Count);

    static NodePaths Nodes(string yaml)
    {
        var nodes = new NodePaths([], [], [], [], [], [], []);
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count > 0) Walk(stream.Documents[0].RootNode, YamlPath.Root, nodes);
        return nodes;
    }

    static void Walk(YamlNode node, YamlPath path, NodePaths nodes)
    {
        if (path.Last is { IsKey: true }) nodes.Keys.Add(path);
        if (path.Last is { IsKey: false }) nodes.Items.Add(path);
        switch (node)
        {
            case YamlScalarNode:
                nodes.Scalars.Add(path);
                break;
            case YamlSequenceNode list:
                nodes.Lists.Add(path);
                nodes.Count[path] = list.Children.Count;
                for (var i = 0; i < list.Children.Count; i++) Walk(list.Children[i], path.Item(i), nodes);
                break;
            case YamlMappingNode map:
                nodes.Mappings.Add(path);
                if (path.Last is { IsKey: false } && map.Children.Any(e => e.Key is YamlScalarNode { Value: "children" }))
                    nodes.Groups.Add(path);
                foreach (var (key, value) in map.Children)
                    if (key is YamlScalarNode { Value: { Length: > 0 } name }) Walk(value, path.Child(name), nodes);
                break;
        }
    }

    static string Fixture(string name, [CallerFilePath] string file = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "Fixtures", name + ".yaml")).Replace("\r\n", "\n");
}
