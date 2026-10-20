using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Marquee.Core.Themes.Schema;
using Marquee.SchemaDocs;

namespace Marquee.Core.Tests;

/// The reference and the JSON Schema the catalog generates are committed, and stay what the catalog writes today.
public class ThemeSchemaDocsTests
{
    static string Repository([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    [Theory]
    [InlineData(GeneratedDocs.ReferencePath)]
    [InlineData(GeneratedDocs.SchemaPath)]
    public void Generated_file_is_up_to_date(string path)
    {
        var expected = GeneratedDocs.Files().Single(f => f.Path == path).Content;
        var full = Path.Combine(Repository(), path);
        Assert.True(File.Exists(full) && GeneratedDocs.SameText(File.ReadAllText(full), expected),
            $"{path} is out of date with the theme schema catalog (ThemeSchema). Regenerate it from the repository's folder "
            + $"with: {GeneratedDocs.Command}");
    }

    [Fact]
    public void Json_schema_names_every_catalog_key()
    {
        var schema = JsonNode.Parse(JsonSchemaWriter.Write())!;
        var definitions = schema["definitions"]!;

        void Names(JsonNode? node, string id, IEnumerable<SchemaKey> keys)
        {
            Assert.True(node?["properties"] is JsonObject, $"layout.schema.json has no definition for '{id}'");
            var properties = node!["properties"]!.AsObject();
            foreach (var key in keys)
                Assert.True(properties.ContainsKey(key.Name), $"layout.schema.json's '{id}' doesn't name '{key.Name}'");
        }

        Names(schema, "layout", ThemeSchema.Layout.Keys);
        foreach (var block in ThemeSchema.Blocks.Where(b => b != ThemeSchema.Layout))
            Names(definitions[block.Id], block.Id, block.Keys);
        foreach (var union in ThemeSchema.Unions)
            foreach (var variant in union.Variants)
                Names(definitions[variant.Id], variant.Id, union.KeysOf(variant));
    }

    /// Every $ref points at a definition, so the schema never sends an editor to nothing.
    [Fact]
    public void Json_schema_references_resolve()
    {
        var schema = JsonNode.Parse(JsonSchemaWriter.Write())!;
        var definitions = schema["definitions"]!.AsObject();
        var refs = new List<string>();

        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                        if (key == "$ref") refs.Add(value!.GetValue<string>());
                        else Walk(value);
                    break;
                case JsonArray array:
                    foreach (var item in array) Walk(item);
                    break;
            }
        }

        Walk(schema);
        Assert.NotEmpty(refs);
        foreach (var target in refs)
            Assert.True(target.StartsWith("#/definitions/", StringComparison.Ordinal) && definitions.ContainsKey(target["#/definitions/".Length..]),
                $"layout.schema.json refers to {target}, which it doesn't define");
    }

    /// So VS Code's YAML extension checks the examples, and anyone who copies one gets the line too.
    [Fact]
    public void Example_themes_name_the_schema()
    {
        var themes = Directory.GetDirectories(Path.Combine(Repository(), "themes"), "example-*");
        Assert.NotEmpty(themes);
        foreach (var theme in themes)
            Assert.Equal("# yaml-language-server: $schema=../../docs/layout.schema.json",
                File.ReadLines(Path.Combine(theme, "layout.yaml")).First());
    }

    /// As the parser has it, only a line shape may be 0 wide or high: the size takes 0, then 1 unless `shape: line`.
    [Fact]
    public void Json_schema_lets_only_a_line_shape_be_0_wide()
    {
        var shape = JsonNode.Parse(JsonSchemaWriter.Write())!["definitions"]!["widget.shape"]!;
        Assert.Equal(0, shape["properties"]!["size"]!["items"]!["minimum"]!.GetValue<int>());

        var line = Assert.Single(shape["allOf"]!.AsArray(), form => form!["if"]!["properties"]!["shape"]!["const"]!.GetValue<string>() == "line");
        Assert.Equal(1, line!["else"]!["properties"]!["size"]!["items"]!["minimum"]!.GetValue<int>());
    }
}
