using System.Globalization;

namespace Marquee.Core.Themes.Editing;

/// A YAML value the editor builds, for YamlWriter to write in the theme's style: a single value (YamlText), a list
/// (YamlList) or a mapping of keys to values, in order (YamlMap). A theme's values are text, which LayoutParser reads,
/// so a number is its text too. The editor makes a new widget from the catalog's example (Parse), changes it (With)
/// and writes it (YamlWriter).
public abstract record YamlValue
{
    public static implicit operator YamlValue(string value) => new YamlText(value);

    /// A number as a theme writes it: invariant, as short as it reads back exactly (`30`, `-12.5`).
    public static YamlText Number(double value) => new(value.ToString(CultureInfo.InvariantCulture));

    /// The value `yaml` holds, without its quoting, layout or comments. Throws LayoutEditException for text that isn't
    /// exactly one value, uses an anchor or alias, or has a key that isn't a single value.
    public static YamlValue Parse(string yaml)
    {
        try
        {
            return From(YamlFragment.Parse(yaml).Tree);
        }
        catch (SpliceException ex)
        {
            throw new LayoutEditException(ex.Message);
        }
    }

    /// The model of a parsed value; YamlFragment has already refused anchors and aliases. Tags are left out: YamlWriter
    /// doesn't write them, and LayoutParser reads a value's text, not its tag.
    internal static YamlValue From(YamlTree tree) => tree switch
    {
        YamlTreeScalar scalar => new YamlText(scalar.Value),
        YamlTreeList list => new YamlList(list.Items.Select(From).ToList()),
        YamlTreeMap map => new YamlMap(map.Entries.Select(e => new KeyValuePair<string, YamlValue>(
            e.Key is YamlTreeScalar key ? key.Value : throw new SpliceException("The new value has a key that is a list or mapping."),
            From(e.Value))).ToList()),
        _ => throw new SpliceException("The new value can't use an anchor (&) or alias (*)."),
    };
}

/// A single value: `30`, `bold`, `CPU {cpu.load:0}%`.
public sealed record YamlText(string Value) : YamlValue;

public sealed record YamlList(IReadOnlyList<YamlValue> Items) : YamlValue;

/// Keys and their values, in the order they are written.
public sealed record YamlMap(IReadOnlyList<KeyValuePair<string, YamlValue>> Entries) : YamlValue
{
    public static YamlMap Empty { get; } = new([]);

    /// The value of `key`, or null when the mapping hasn't it.
    public YamlValue? this[string key] => Entries.FirstOrDefault(e => e.Key == key).Value;

    /// The mapping with `key` set to `value`: in its place when it is there already, else last.
    public YamlMap With(string key, YamlValue value)
    {
        var entries = Entries.ToList();
        var at = entries.FindIndex(e => e.Key == key);
        if (at < 0) entries.Add(new(key, value));
        else entries[at] = new(key, value);
        return new YamlMap(entries);
    }

    public YamlMap Without(string key) => new(Entries.Where(e => e.Key != key).ToList());
}
