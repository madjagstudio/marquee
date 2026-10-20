using System.Runtime.CompilerServices;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Core.Tests.Editing;

/// What holds for every list edit, tried on every widget list of each example theme (the widgets and every group's
/// children), with LF and CRLF line breaks (Edits): the theme still parses, and every widget the edit doesn't involve
/// parses to the same WidgetSpec as before, wherever the edit moved it in its list. A widget is involved when it is
/// inserted, removed, moved, wrapped or unwrapped, or is a group whose children change. Inserts use the catalog's new
/// widgets, as the editor does; the moves and wraps are random, from a fixed seed so a failure repeats.
public class LayoutDocumentListPropertyTests
{
    static readonly string[] Names = ["example-agents", "example-landscape", "example-portrait", "example-showcase"];

    public static TheoryData<string> Examples => new(Names);

    const string Header = "type: group\nat: [10, 10]\nsize: [200, 200]";

    /// A list edit, the lists whose items it changes (so the groups above them change too), and where each widget it
    /// doesn't involve ends up: null for the ones it does.
    sealed record ListEdit(string Name, Func<LayoutDocument, LayoutDocument> Apply, IReadOnlyList<YamlPath> Lists, Func<YamlPath, YamlPath?> After);

    [Theory]
    [MemberData(nameof(Examples))]
    public void A_list_edit_leaves_every_widget_it_doesnt_involve_as_it_was(string name)
    {
        var yaml = Fixture(name);
        var before = LayoutParser.Parse(yaml);
        var widgets = Paths(before.Widgets, YamlPath.Root.Child("widgets")).ToList();
        var compared = 0;

        foreach (var edit in ListEdits(before, new Random(Array.IndexOf(Names, name))))
        {
            var after = LayoutParser.Parse(Edits.Apply(yaml, edit.Apply));
            foreach (var path in widgets)
            {
                if (edit.Lists.Any(list => IsAbove(path, list)) || edit.After(path) is not { } moved) continue;
                var difference = Record.Exception(() => Structural.Equal(At(before, path), At(after, moved)));
                Assert.True(difference is null, $"{edit.Name}: {path} (now {moved}) changed. {difference?.Message}");
                compared++;
            }
        }

        Assert.True(compared > widgets.Count * 10, $"Only {compared} widgets were compared");
    }

    static IEnumerable<ListEdit> ListEdits(ThemeSpec spec, Random random)
    {
        var lists = Lists(spec.Widgets, YamlPath.Root.Child("widgets")).ToList();
        string[] examples = [.. ThemeSchema.Widgets.Variants.Where(v => v.Name != "video").Select(v => v.Example!)];

        foreach (var (list, count) in lists)
        {
            foreach (var index in new[] { 0, count / 2, count }.Distinct())
            {
                var item = examples[random.Next(examples.Length)];
                yield return new($"insert {list} {index}", d => d.InsertItem(list, index, item), [list], p => Shifted(p, list, index, 1));
            }

            for (var index = 0; index < count; index++)
            {
                var item = list.Item(index);
                yield return new($"remove-item {item}", d => d.RemoveItem(item), [list], p => Removed(p, item));

                // Somewhere else in its own list, or into another list that isn't inside it.
                var targets = lists.Where(l => !IsAbove(item, l.Path)).ToList();
                var (to, toCount) = targets[random.Next(targets.Count)];
                var places = to == list ? toCount : toCount + 1;
                var at = random.Next(places);
                if (to == list && at == index) at = (at + 1) % places;
                yield return new($"move {item} {to} {at}", d => d.MoveItem(item, to, at), [list, to],
                    p => Removed(p, item) is { } q ? Shifted(q, AfterRemoving(to, item), at, 1) : null);
            }

            foreach (var size in new[] { 1, 2, 3 }.Where(size => size <= count))
            {
                var chosen = Enumerable.Range(0, count).OrderBy(_ => random.Next()).Take(size).Order().ToList();
                yield return new($"wrap {list} {string.Join(',', chosen)}", d => d.WrapInGroup(list, chosen, Header), [list],
                    p => Wrapped(p, list, chosen));
            }
        }

        foreach (var (group, children) in Groups(spec.Widgets, YamlPath.Root.Child("widgets")))
            yield return new($"unwrap {group}", d => d.UnwrapGroup(group), [group.Parent!], p => Unwrapped(p, group, children));
    }

    /// The path once `by` items have gone in at `from` of `list` (or, with a negative `by`, gone from before it).
    static YamlPath Shifted(YamlPath path, YamlPath list, int from, int by) =>
        IndexIn(path, list) is { } index && index >= from ? WithIndex(path, list, index + by) : path;

    static YamlPath? Removed(YamlPath path, YamlPath item) =>
        IsAbove(item, path) || path == item ? null : Shifted(path, item.Parent!, item.Last!.Value.Index + 1, -1);

    static YamlPath? Wrapped(YamlPath path, YamlPath list, List<int> chosen)
    {
        if (IndexIn(path, list) is not { } index) return path;
        if (chosen.Contains(index)) return null;
        return WithIndex(path, list, index - chosen.Count(c => c < index) + (index > chosen[0] ? 1 : 0));
    }

    static YamlPath? Unwrapped(YamlPath path, YamlPath group, int children)
    {
        if (path == group || IsAbove(group, path)) return null;
        return Shifted(path, group.Parent!, group.Last!.Value.Index + 1, children - 1);
    }

    /// The path of `path` once the item at `removed` has gone, for a list named before the move.
    static YamlPath AfterRemoving(YamlPath path, YamlPath removed) =>
        IndexIn(path, removed.Parent!) is { } index && index > removed.Last!.Value.Index ? WithIndex(path, removed.Parent!, index - 1) : path;

    /// The index `path` passes through in `list`, when it leads through one of its items.
    static int? IndexIn(YamlPath path, YamlPath list) =>
        IsAbove(list, path) && path.Segments[list.Segments.Count] is { IsKey: false } segment ? segment.Index : null;

    static YamlPath WithIndex(YamlPath path, YamlPath list, int index)
    {
        var result = YamlPath.Root;
        for (var i = 0; i < path.Segments.Count; i++)
        {
            var segment = path.Segments[i];
            result = segment.Key is { } key ? result.Child(key) : result.Item(i == list.Segments.Count ? index : segment.Index);
        }

        return result;
    }

    /// True when `above` is a proper prefix of `path`.
    static bool IsAbove(YamlPath above, YamlPath path) =>
        path.Segments.Count > above.Segments.Count && path.Segments.Take(above.Segments.Count).SequenceEqual(above.Segments);

    static IEnumerable<YamlPath> Paths(IReadOnlyList<WidgetSpec> widgets, YamlPath list)
    {
        for (var i = 0; i < widgets.Count; i++)
        {
            yield return list.Item(i);
            if (widgets[i] is GroupWidget group)
                foreach (var child in Paths(group.Children, list.Item(i).Child("children"))) yield return child;
        }
    }

    /// The widget lists, as their paths and lengths: `list` itself and every group's children below it.
    static IEnumerable<(YamlPath Path, int Count)> Lists(IReadOnlyList<WidgetSpec> widgets, YamlPath list) =>
        Groups(widgets, list).Select(g => (g.Group.Child("children"), g.Children)).Prepend((list, widgets.Count));

    static IEnumerable<(YamlPath Group, int Children)> Groups(IReadOnlyList<WidgetSpec> widgets, YamlPath list)
    {
        for (var i = 0; i < widgets.Count; i++)
        {
            if (widgets[i] is not GroupWidget group) continue;
            yield return (list.Item(i), group.Children.Count);
            foreach (var inner in Groups(group.Children, list.Item(i).Child("children"))) yield return inner;
        }
    }

    static WidgetSpec At(ThemeSpec spec, YamlPath path) => At(spec.Widgets, path);

    /// The widget at `path`, which runs `widgets[i].children[j]…`, below the list that `widgets` is.
    static WidgetSpec At(IReadOnlyList<WidgetSpec> widgets, YamlPath path)
    {
        var indexes = path.Segments.Where(s => !s.IsKey).Select(s => s.Index).ToList();
        var widget = widgets[indexes[0]];
        foreach (var index in indexes.Skip(1)) widget = ((GroupWidget)widget).Children[index];
        return widget;
    }

    static string Fixture(string name, [CallerFilePath] string file = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "Fixtures", name + ".yaml")).Replace("\r\n", "\n");
}
