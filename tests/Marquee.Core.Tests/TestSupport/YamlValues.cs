using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Tests.TestSupport;

/// A YAML file's values as YamlDotNet reads them, flattened to dotted paths ("theme", "lconnect.handover",
/// "list[0]"). It checks ConfigEditor's round trip for any key, whether or not ConfigParser knows it, and for every edit checks
/// that nothing but the one key changed.
static class YamlValues
{
    public static Dictionary<string, string> Read(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (stream.Documents.Count > 0) Flatten(stream.Documents[0].RootNode, "", values);
        return values;
    }

    /// `after` holds `value` at `key` and every other value of `before` unchanged. A parent that was empty
    /// (`lconnect:` with nothing under it) became the mapping that holds the key.
    public static void AssertOnlyChanged(string before, string after, string key, string value)
    {
        var expected = Read(before);
        if (key.Split('.') is [var parent, _]) expected.Remove(parent);
        expected[key] = value;

        Assert.Equal(expected.OrderBy(e => e.Key), Read(after).OrderBy(e => e.Key));
    }

    static void Flatten(YamlNode node, string path, Dictionary<string, string> values)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                values[path] = scalar.Value ?? "";
                break;
            case YamlMappingNode map:
                foreach (var (key, value) in map.Children)
                    Flatten(value, path.Length == 0 ? ((YamlScalarNode)key).Value! : $"{path}.{((YamlScalarNode)key).Value}", values);
                break;
            case YamlSequenceNode list:
                for (var i = 0; i < list.Children.Count; i++)
                    Flatten(list.Children[i], $"{path}[{i}]", values);
                break;
        }
    }
}
