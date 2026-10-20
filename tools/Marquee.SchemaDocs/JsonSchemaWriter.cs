using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marquee.Core.Themes.Schema;

namespace Marquee.SchemaDocs;

/// docs/layout.schema.json: a JSON Schema (draft-07, which VS Code's YAML extension has supported longest) for
/// layout.yaml, for completion, hover help and checks while a theme is hand-edited. Every block rejects unknown keys, as
/// the parser does; widgets are a oneOf on `type`, and layers and gradients a oneOf on the key each kind must have.
/// It checks less than the parser: a schema can't see that a value widget's format includes {0}, say, so
/// `marquee validate` stays the last word.
static class JsonSchemaWriter
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Keeps °, × and quotes in descriptions readable; the file is only ever read as JSON.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write()
    {
        var definitions = new JsonObject
        {
            ["color"] = new JsonObject
            {
                ["type"] = "string",
                ["pattern"] = ThemeSchema.ColorPattern,
                ["description"] = ThemeSchema.KindHelp(SchemaKind.Color),
            },
            ["paint"] = new JsonObject
            {
                ["description"] = ThemeSchema.KindHelp(SchemaKind.Paint),
                ["oneOf"] = new JsonArray(Ref("color"), Ref(ThemeSchema.Gradient.Id)),
            },
        };

        foreach (var block in ThemeSchema.Blocks.Where(b => b != ThemeSchema.Layout))
            definitions[block.Id] = Object(block, block.Keys);

        foreach (var union in ThemeSchema.Unions)
        {
            definitions[union.Id] = Union(union);
            foreach (var variant in union.Variants)
                definitions[variant.Id] = Object(variant, union.KeysOf(variant), union.Discriminator);
        }

        var schema = new JsonObject { ["$schema"] = "http://json-schema.org/draft-07/schema#", ["title"] = "Marquee theme (layout.yaml)" };
        foreach (var (key, value) in Object(ThemeSchema.Layout, ThemeSchema.Layout.Keys))
            schema[key] = value?.DeepClone();
        schema["definitions"] = definitions;
        return schema.ToJsonString(Options).ReplaceLineEndings("\n") + "\n";
    }

    static JsonObject Union(SchemaUnion union)
    {
        var node = new JsonObject { ["description"] = union.Help, ["type"] = "object" };
        if (union.DiscriminatorKey is { } discriminator)
        {
            // Lets an editor offer the types before the rest of the widget is written.
            node["required"] = new JsonArray(discriminator.Name);
            node["properties"] = new JsonObject { [discriminator.Name] = Value(discriminator) };
        }

        node["oneOf"] = new JsonArray(union.Variants.Select(v => (JsonNode?)Ref(v.Id)).ToArray());
        return node;
    }

    /// `discriminator` is the key whose value names this variant, which only it may have.
    static JsonObject Object(SchemaBlock block, IReadOnlyList<SchemaKey> keys, string? discriminator = null)
    {
        var properties = new JsonObject();
        foreach (var key in keys)
            properties[key.Name] = key.Name == discriminator
                ? new JsonObject { ["const"] = block.Name, ["description"] = key.Help }
                : Value(key);

        var node = new JsonObject { ["description"] = Join(block.Title, block.Help), ["type"] = "object", ["properties"] = properties };
        if (keys.Where(k => k.Required).Select(k => (JsonNode?)k.Name).ToArray() is { Length: > 0 } required)
            node["required"] = new JsonArray(required);
        node["additionalProperties"] = false;
        if (block.ExactlyOneOf.Count > 0)
            node["oneOf"] = new JsonArray(block.ExactlyOneOf.Select(k => (JsonNode?)Requires(k)).ToArray());
        if (block.AtLeastOneOf.Count > 0)
            node["anyOf"] = new JsonArray(block.AtLeastOneOf.Select(k => (JsonNode?)Requires(k)).ToArray());
        var forms = keys.Where(k => k.OnlyFor is not null).GroupBy(k => k.OnlyFor!.Value).Select(OnlyFor)
            .Concat(keys.Where(k => k.MinFor is not null).Select(MinFor))
            .ToArray();
        if (forms.Length > 0)
            node["allOf"] = new JsonArray(forms);
        return node;
    }

    /// Keys that belong to one form of the block, such as an arc shape's `start`, aren't allowed in any other form.
    static JsonNode? OnlyFor(IGrouping<(string Key, string Value), SchemaKey> keys) => new JsonObject
    {
        ["if"] = Form(keys.Key.Key, keys.Key.Value),
        ["else"] = new JsonObject
        {
            ["properties"] = new JsonObject(keys.Select(k => KeyValuePair.Create(k.Name, (JsonNode?)JsonValue.Create(false)))),
        },
    };

    /// A key with a lower minimum in one form, such as a line shape's size, takes the lower one there (Kind gives it
    /// that) and its own Min in every other form.
    static JsonNode? MinFor(SchemaKey key) => new JsonObject
    {
        ["if"] = Form(key.MinFor!.Value.Key, key.MinFor.Value.Value),
        ["else"] = new JsonObject { ["properties"] = new JsonObject { [key.Name] = Kind(key with { MinFor = null }) } },
    };

    /// Matches the form of a block whose `key` is `value`.
    static JsonObject Form(string key, string value) => new()
    {
        ["properties"] = new JsonObject { [key] = new JsonObject { ["const"] = value } },
        ["required"] = new JsonArray(key),
    };

    static JsonObject Requires(string key) => new() { ["required"] = new JsonArray(key) };

    /// One key's schema, with its help as the description and its default.
    static JsonObject Value(SchemaKey key)
    {
        var node = Kind(key);
        if (key.TakesFalse) node = new JsonObject { ["anyOf"] = new JsonArray(node, new JsonObject { ["const"] = false }) };
        // A $ref's siblings are ignored in draft-07, so a description goes on an allOf around it.
        if (node.ContainsKey("$ref")) node = new JsonObject { ["allOf"] = new JsonArray(node) };
        node["description"] = ReferenceWriter.Help(key);
        if (key.Default is { } value) node["default"] = Default(key, value);
        return node;
    }

    static JsonObject Kind(SchemaKey key) => key.Kind switch
    {
        SchemaKind.Number => Bounded(new JsonObject { ["type"] = "number" }, key.Min, key.Max),
        SchemaKind.Integer => Bounded(new JsonObject { ["type"] = "integer" }, key.Min, key.Max),
        SchemaKind.Enum => new JsonObject { ["enum"] = Strings(key.Values) },
        SchemaKind.FontWeight => new JsonObject
        {
            ["anyOf"] = new JsonArray(Bounded(new JsonObject { ["type"] = "integer" }, key.Min, key.Max), new JsonObject { ["enum"] = Strings(key.Values) }),
        },
        SchemaKind.ThemeFile when key.FileTypes.Count > 0 => new JsonObject { ["type"] = "string", ["pattern"] = Extensions(key.FileTypes) },
        // A shape's size may be 0 one way, for a line: the lowest Min of any form, which MinFor raises in the others.
        SchemaKind.Size when (key.MinFor?.Min ?? key.Min) is { } min => Pair(new JsonObject { ["type"] = "number", ["minimum"] = Number(min) }),
        SchemaKind.Block => Ref(key.Of!),
        SchemaKind.List => Bounded(new JsonObject
        {
            ["type"] = "array",
            ["items"] = key.Of is { } of ? Ref(of) : Plain(key.ItemKind!.Value),
        }, key.MinItems, key.MaxItems, "minItems", "maxItems"),
        SchemaKind.Map => new JsonObject { ["type"] = "object", ["additionalProperties"] = Ref(key.Of!) },
        SchemaKind.Uniforms => Bounded(new JsonObject
        {
            ["type"] = "object",
            ["propertyNames"] = new JsonObject
            {
                ["pattern"] = ThemeSchema.UniformNamePattern,
                ["not"] = new JsonObject { ["enum"] = Strings(ThemeSchema.ReservedUniforms) },
            },
            ["additionalProperties"] = new JsonObject
            {
                ["anyOf"] = new JsonArray(
                    Plain(SchemaKind.Expression),
                    new JsonObject
                    {
                        ["type"] = "array", ["items"] = Plain(SchemaKind.Expression), ["minItems"] = 1, ["maxItems"] = ThemeSchema.MaxUniformValues,
                    }),
            },
        }, key.MinItems, key.MaxItems, "minProperties", "maxProperties"),
        var kind => Plain(kind),
    };

    /// A value with nothing more to it than its kind. Keys the parser reads as text take any scalar YAML writes without
    /// quotes too, so `equals: 3` and `text: 2024` don't show as errors.
    static JsonObject Plain(SchemaKind kind) => kind switch
    {
        SchemaKind.Boolean => new JsonObject { ["type"] = "boolean" },
        SchemaKind.String => new JsonObject { ["type"] = Strings(["string", "number", "boolean"]) },
        SchemaKind.Expression or SchemaKind.Template => new JsonObject { ["type"] = Strings(["string", "number"]) },
        SchemaKind.Color => Ref("color"),
        SchemaKind.Paint => Ref("paint"),
        SchemaKind.Point => Pair(new JsonObject { ["type"] = "number" }),
        SchemaKind.Size => Pair(new JsonObject { ["type"] = "number", ["minimum"] = 1 }),
        SchemaKind.Style => new JsonObject
        {
            ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "string" }, Ref(ThemeSchema.Style.Id)),
        },
        SchemaKind.FontFamily or SchemaKind.SensorKey or SchemaKind.Format or SchemaKind.ThemeFile or SchemaKind.StyleName =>
            new JsonObject { ["type"] = "string" },
        _ => throw new InvalidOperationException($"JsonSchemaWriter has no plain schema for {kind}"),
    };

    static JsonObject Pair(JsonObject item) => new() { ["type"] = "array", ["items"] = item, ["minItems"] = 2, ["maxItems"] = 2 };

    static JsonObject Bounded(JsonObject node, double? min, double? max, string minKey = "minimum", string maxKey = "maximum")
    {
        if (min is { } a) node[minKey] = Number(a);
        if (max is { } b) node[maxKey] = Number(b);
        return node;
    }

    static JsonNode Default(SchemaKey key, string value) => key.Kind switch
    {
        SchemaKind.Number or SchemaKind.Integer => Number(double.Parse(value, CultureInfo.InvariantCulture)),
        SchemaKind.Boolean => JsonValue.Create(value == "true"),
        _ => JsonValue.Create(value),
    };

    /// Whole numbers as integers, so the file reads 30 rather than 30.0.
    static JsonNode Number(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15 ? JsonValue.Create((long)value) : JsonValue.Create(value);

    static JsonObject Ref(string id) => new() { ["$ref"] = $"#/definitions/{id}" };

    static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    /// A case-insensitive match on the extension, which JSON Schema's regular expressions can only spell out.
    static string Extensions(IEnumerable<string> extensions) =>
        "\\.(" + string.Join("|", extensions.Select(e => string.Concat(e.TrimStart('.').Select(c =>
            char.IsLetter(c) ? $"[{char.ToLowerInvariant(c)}{char.ToUpperInvariant(c)}]" : c.ToString())))) + ")$";

    static string Join(string title, string help) => help.Length == 0 ? title : $"{title}: {help}";
}
