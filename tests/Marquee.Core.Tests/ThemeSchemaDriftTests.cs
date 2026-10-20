using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Themes.Schema;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// ThemeSchema describes what the parser does: the same keys in every block, in both directions, and the same required
/// keys, ranges, enum values and defaults. The key lists come from the parsers themselves; everything else is checked
/// by parsing small layouts built from the catalog (JSON, which is YAML), so the test reads the parser's behaviour rather
/// than a second copy of it.
public class ThemeSchemaDriftTests
{
    /// What the parser accepts in each block, by catalog id. A union's variants are checked one by one where the parser
    /// knows which keys each takes, and as a whole where it doesn't (a gradient rejects `angle` on radial by hand, which
    /// the probes below check). A new block needs an entry here.
    static Dictionary<string, IReadOnlyList<string>> ParserKeys()
    {
        var keys = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["layout"] = LayoutParser.Sections,
            ["canvas"] = LayoutParser.CanvasKeys,
            ["style"] = StyleTable.Keys,
            ["style.stroke"] = StyleTable.StrokeKeys,
            ["style.shadow"] = StyleTable.ShadowKeys,
            ["style.glow"] = StyleTable.GlowKeys,
            ["style.pulse"] = StyleTable.PulseKeys,
            ["gradient"] = PaintParser.GradientKeys,
            ["when"] = WidgetParser.RuleKeys,
            ["ring"] = RingParser.Keys,
            ["ring.when"] = RingParser.RuleKeys,
            ["gauge.needle"] = DialParser.NeedleKeys,
            ["gauge.ticks"] = DialParser.TickKeys,
            ["graph.grid"] = GraphParser.GridKeys,
            ["graph.series"] = GraphParser.SeriesKeys,
            ["donut.slice"] = DonutParser.SliceKeys,
        };
        foreach (var (type, _) in WidgetParser.Types)
            keys[$"widget.{type}"] = [.. WidgetParser.Common, .. WidgetParser.TypeKeys[type]];
        foreach (var kind in BackgroundParser.Kinds)
            keys[$"layer.{kind}"] = BackgroundParser.LayerKeys
                .Where(key => key == kind || !BackgroundParser.Kinds.Contains(key))
                .Where(key => BackgroundParser.KindOnly.All(only => only.Key != key || only.Kinds.Contains(kind)))
                .ToList();
        return keys;
    }

    /// The catalog's keys for a parser entry's id: a plain block's, a variant's with the common keys, or every variant's.
    static IReadOnlyList<string>? CatalogKeys(string id)
    {
        if (ThemeSchema.Blocks.FirstOrDefault(b => b.Id == id) is { } block) return block.Keys.Select(k => k.Name).ToList();
        if (ThemeSchema.Union(id) is { } union)
            return union.Variants.SelectMany(union.KeysOf).Select(k => k.Name).Distinct().ToList();
        foreach (var u in ThemeSchema.Unions)
            if (u.Variants.FirstOrDefault(v => v.Id == id) is { } variant)
                return u.KeysOf(variant).Select(k => k.Name).ToList();
        return null;
    }

    [Fact]
    public void Every_block_accepts_the_keys_the_parser_does()
    {
        var problems = new List<string>();
        var parser = ParserKeys();
        foreach (var (id, accepted) in parser)
        {
            if (CatalogKeys(id) is not { } described)
            {
                problems.Add($"{id}: the parser reads this block, but the catalog has no block or union '{id}'");
                continue;
            }

            foreach (var key in accepted.Except(described))
                problems.Add($"{id}: the parser accepts '{key}', which the catalog doesn't describe");
            foreach (var key in described.Except(accepted))
                problems.Add($"{id}: the catalog describes '{key}', which the parser rejects");
        }

        // Every block the catalog describes is compared above: a plain block or variant by its own id, or its union's.
        var compared = ThemeSchema.Blocks.Select(b => b.Id)
            .Concat(ThemeSchema.Unions.SelectMany(u => parser.ContainsKey(u.Id) ? [u.Id] : u.Variants.Select(v => v.Id)));
        foreach (var id in compared.Where(id => !parser.ContainsKey(id)))
            problems.Add($"{id}: the catalog describes this block, but the parser has no keys for it (or ParserKeys misses it)");

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Rules_take_the_parsers_conditions_and_changes()
    {
        Assert.Equal(WidgetParser.Conditions, ThemeSchema.When.ExactlyOneOf);
        Assert.Equal(WidgetParser.Conditions, ThemeSchema.RingWhen.ExactlyOneOf);
        Assert.Equal(RingParser.Overrides, ThemeSchema.RingWhen.AtLeastOneOf);
    }

    /// The JSON Schema checks colors with ThemeSchema.ColorPattern; the parser with TryParseColor.
    [Theory]
    [InlineData("#FFF")]
    [InlineData("#a1b2c3")]
    [InlineData("#11223344")]
    [InlineData("transparent")]
    [InlineData("#12")]
    [InlineData("#1234")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#+12345")]
    [InlineData("FFFFFF")]
    [InlineData("red")]
    [InlineData("Transparent")]
    [InlineData("#FFFFFF ")]
    public void Color_pattern_accepts_what_the_parser_does(string text) =>
        Assert.Equal(PaintParser.TryParseColor(text, out _), Regex.IsMatch(text, ThemeSchema.ColorPattern));

    [Fact]
    public void Every_key_parses_with_a_value_of_its_kind() =>
        Check(Probes(), (place, key) => Parses(place, place.With(key.Name, place.Names(key) ? place.Block.Name : Sample(key)), key.Name));

    /// A key the catalog says also takes false, such as a gauge's tick labels, parses with it.
    [Fact]
    public void Keys_that_take_false_parse_it() =>
        Check(Probes().Where(p => p.Key.TakesFalse), (place, key) => Parses(place, place.With(key.Name, JsonValue.Create(false)), $"{key.Name}: false"));

    [Fact]
    public void Every_default_parses() =>
        Check(Probes().Where(p => p.Key.Default is not null), (place, key) => Parses(place, place.With(key.Name, Default(key)), key.Name));

    /// Leaving a key out means the same as writing its default. Style keys are left unset by the parser and filled in
    /// by the renderer, so Style_defaults_are_StyleDefaults checks those.
    [Fact]
    public void Leaving_a_key_out_gives_its_default()
    {
        Check(Probes().Where(p => p.Key.Default is not null && !IsStyle(p.Place.Block)), (place, key) =>
        {
            var omitted = Layouts.Parse(place.Layout(place.Base(key.Name)));
            var written = Layouts.Parse(place.Layout(place.With(key.Name, Default(key))));
            var difference = Record.Exception(() => Structural.Equal(omitted, written));
            Assert.True(difference is null,
                $"{place.Block.Id}.{key.Name}: leaving it out isn't the same as writing its default {key.Default}: {difference?.Message}");
        });
    }

    /// The renderer's fallbacks for style keys with a constant, by StyleDefaults field. A new constant needs an entry.
    static readonly (string Field, string Block, string Key, Func<StyleSpec, object?> Read)[] StyleConstants =
    [
        (nameof(StyleDefaults.Font), "style", "font", s => s.Font),
        (nameof(StyleDefaults.Size), "style", "size", s => s.Size),
        (nameof(StyleDefaults.Weight), "style", "weight", s => s.Weight),
        (nameof(StyleDefaults.Color), "style", "color", s => s.Color),
        (nameof(StyleDefaults.Opacity), "style", "opacity", s => s.Opacity),
        (nameof(StyleDefaults.Radius), "style", "radius", s => s.Radius),
        (nameof(StyleDefaults.Padding), "style", "padding", s => s.Padding),
        (nameof(StyleDefaults.Align), "style", "align", s => s.Align),
        (nameof(StyleDefaults.Blend), "style", "blend", s => s.Blend),
        (nameof(StyleDefaults.StrokeWidth), "style.stroke", "width", s => s.Stroke?.Width),
        (nameof(StyleDefaults.StrokeCap), "style.stroke", "cap", s => s.Stroke?.Cap),
        (nameof(StyleDefaults.ShadowX), "style.shadow", "x", s => s.Shadow?.X),
        (nameof(StyleDefaults.ShadowY), "style.shadow", "y", s => s.Shadow?.Y),
        (nameof(StyleDefaults.ShadowBlur), "style.shadow", "blur", s => s.Shadow?.Blur),
        (nameof(StyleDefaults.GlowRadius), "style.glow", "radius", s => s.Glow?.Radius),
        (nameof(StyleDefaults.PulseProperty), "style.pulse", "property", s => s.Pulse?.Target),
        (nameof(StyleDefaults.PulsePeriodMs), "style.pulse", "period", s => s.Pulse?.Period),
        (nameof(StyleDefaults.PulseMin), "style.pulse", "min", s => s.Pulse?.Min),
        (nameof(StyleDefaults.PulseMax), "style.pulse", "max", s => s.Pulse?.Max),
        (nameof(StyleDefaults.LetterSpacing), "style", "letter_spacing", s => s.LetterSpacing),
        (nameof(StyleDefaults.LineHeight), "style", "line_height", s => s.LineHeight),
        (nameof(StyleDefaults.Wrap), "style", "wrap", s => s.Wrap),
    ];

    [Fact]
    public void Style_defaults_are_StyleDefaults()
    {
        var fields = typeof(StyleDefaults).GetFields(BindingFlags.Public | BindingFlags.Static);
        Assert.Equal(fields.Select(f => f.Name).Order(), StyleConstants.Select(c => c.Field).Concat(WidgetStyleConstants.Select(c => c.Field)).Order());

        foreach (var (field, blockId, keyName, read) in StyleConstants)
        {
            var block = ThemeSchema.Block(blockId)!;
            var key = block.Key(keyName) ?? throw new InvalidOperationException($"{blockId} has no key '{keyName}'");
            Assert.True(key.Default is not null, $"{blockId}.{keyName} has no default, but StyleDefaults.{field} is its fallback");

            var place = new Place(block, null);
            var style = Layouts.Parse(place.Layout(place.With(keyName, Default(key)))).Widgets[0].Style;
            var expected = fields.Single(f => f.Name == field).GetValue(null);
            Assert.True(Equals(expected, read(style)), $"{blockId}.{keyName}: the catalog's default {key.Default} reads as {read(style)}, but StyleDefaults.{field} is {expected}");
        }
    }

    /// Every style key the catalog gives a default has a StyleDefaults constant, so a painter's fallback can't drift
    /// from the catalog unnoticed.
    [Fact]
    public void Every_style_default_has_a_StyleDefaults_constant()
    {
        var described = StyleBlocks.SelectMany(b => b.Keys.Where(k => k.Default is not null).Select(k => $"{b.Id}.{k.Name}"));
        Assert.Equal(described.Order(), StyleConstants.Select(c => $"{c.Block}.{c.Key}").Order());
    }

    /// The renderer's fallbacks that differ for one widget type, by StyleDefaults field. A new one needs an entry.
    static readonly (string Field, string Type, string Block, string Key, Func<StyleSpec, object?> Read)[] WidgetStyleConstants =
    [
        (nameof(StyleDefaults.GraphStrokeWidth), "graph", "style.stroke", "width", s => s.Stroke?.Width),
        (nameof(StyleDefaults.GraphStrokeCap), "graph", "style.stroke", "cap", s => s.Stroke?.Cap),
    ];

    /// A key's default on one widget type reads as that type's StyleDefaults constant, and every one has a constant.
    [Fact]
    public void Defaults_for_one_widget_type_are_StyleDefaults()
    {
        var described = StyleBlocks.SelectMany(b => b.Keys.SelectMany(k => k.DefaultFor.Keys.Select(type => $"{b.Id}.{k.Name} on {type}")));
        Assert.Equal(WidgetStyleConstants.Select(c => $"{c.Block}.{c.Key} on {c.Type}").Order(), described.Order());

        foreach (var (field, type, blockId, keyName, read) in WidgetStyleConstants)
        {
            var block = ThemeSchema.Block(blockId)!;
            var key = block.Key(keyName)!;
            Assert.Contains(type, ThemeSchema.Widgets.Names);

            var place = new Place(block, null);
            var written = Default(key with { Default = key.DefaultFor[type] });
            var style = Layouts.Parse(place.Layout(place.With(keyName, written))).Widgets[0].Style;
            var expected = typeof(StyleDefaults).GetField(field)!.GetValue(null);
            Assert.True(Equals(expected, read(style)),
                $"{blockId}.{keyName}: the catalog's default on a {type} {key.DefaultFor[type]} reads as {read(style)}, but StyleDefaults.{field} is {expected}");
        }
    }

    /// The table AppliesTo comes from names every widget type, in order, and only style keys; and every style key is
    /// used by some type.
    [Fact]
    public void Style_keys_say_which_widget_types_use_them()
    {
        Assert.Equal(ThemeSchema.Widgets.Names, ThemeSchema.WidgetStyles.Select(t => t.Type));

        var paths = new List<string>();
        foreach (var block in StyleBlocks)
        {
            foreach (var key in block.Keys)
            {
                var path = block.Id == "style" ? key.Name : $"{block.Name}.{key.Name}";
                paths.Add(path);
                var everyWidget = ThemeSchema.EveryWidgetStyles.Any(k => k == path || path.StartsWith(k + ".", StringComparison.Ordinal));
                Assert.True(key.AppliesTo.Count > 0 || everyWidget, $"No widget type uses style key '{path}': add it to the types that draw with it in ThemeSchema.WidgetStyles, or to EveryWidgetStyles");
                Assert.All(key.AppliesTo, type => Assert.Contains(type, ThemeSchema.Widgets.Names));
            }
        }

        var blocks = StyleBlocks.Select(b => b.Name).Where(name => name != "style").ToList();
        foreach (var (type, keys) in ThemeSchema.WidgetStyles)
            foreach (var key in keys.Concat(ThemeSchema.EveryWidgetStyles))
                Assert.True(paths.Contains(key) || blocks.Contains(key), $"ThemeSchema.WidgetStyles gives {type} '{key}', which isn't a style key");
    }

    static IEnumerable<SchemaBlock> StyleBlocks => ThemeSchema.Blocks.Where(IsStyle);

    [Fact]
    public void Required_keys_are_required() =>
        Check(Probes().Where(p => p.Key.Required), (place, key) =>
        {
            var probe = place.Base();
            probe.Remove(key.Name);
            Fails(place, probe, $"leaving out required '{key.Name}'");
        });

    [Fact]
    public void Numbers_take_their_range_and_no_more() =>
        Check(Probes().Where(p => p.Key.Kind is SchemaKind.Number or SchemaKind.Integer or SchemaKind.FontWeight), (place, key) =>
        {
            var step = key.Kind == SchemaKind.Number ? 0.01 : 1;
            if (key.Min is { } min)
            {
                Parses(place, place.With(key.Name, min), $"{key.Name}: {min}");
                Fails(place, place.With(key.Name, min - step), $"{key.Name}: {min - step}");
            }

            if (key.Max is { } max)
            {
                Parses(place, place.With(key.Name, max), $"{key.Name}: {max}");
                Fails(place, place.With(key.Name, max + step), $"{key.Name}: {max + step}");
            }
        });

    /// A list, a map or a shader's uniforms takes as few and as many entries as the catalog says, and no fewer or more.
    [Fact]
    public void Lists_take_their_count_and_no_more() =>
        Check(Probes().Where(p => p.Key.MinItems is not null || p.Key.MaxItems is not null), (place, key) =>
        {
            if (key.MinItems is { } min)
            {
                Parses(place, place.With(key.Name, Items(key, min)), $"{key.Name} with {min} entries");
                if (min > 0) Fails(place, place.With(key.Name, Items(key, min - 1)), $"{key.Name} with {min - 1} entries");
            }

            if (key.MaxItems is { } max)
            {
                Parses(place, place.With(key.Name, Items(key, max)), $"{key.Name} with {max} entries");
                Fails(place, place.With(key.Name, Items(key, max + 1)), $"{key.Name} with {max + 1} entries");
            }
        });

    /// A block that needs exactly one, or at least one, of a group of keys refuses none of them, and one that needs
    /// exactly one refuses two.
    [Fact]
    public void Groups_of_keys_are_needed_as_the_catalog_says() =>
        Check(Probes().Select(p => p.Place).DistinctBy(p => p.Block.Id).Where(p => p.Block.ExactlyOneOf.Count > 0 || p.Block.AtLeastOneOf.Count > 0), place =>
        {
            foreach (var group in new[] { place.Block.ExactlyOneOf, place.Block.AtLeastOneOf }.Where(g => g.Count > 0))
            {
                var none = place.Base();
                foreach (var key in group) none.Remove(key);
                Fails(place, none, $"none of {string.Join(", ", group)}");
            }

            if (place.Block.ExactlyOneOf is { Count: > 1 } one)
            {
                var two = place.Base();
                foreach (var key in one.Take(2)) two[key] = Sample(place.Block.Key(key)!);
                Fails(place, two, $"both {one[0]} and {one[1]}");
            }
        });

    /// A whole number refuses a fraction that a number takes, and a single sensor key refuses the expression that any
    /// other source takes.
    [Fact]
    public void Keys_take_only_their_kind_of_number_or_source() =>
        Check(Probes().Where(p => p.Key.Kind is SchemaKind.Number or SchemaKind.Integer or SchemaKind.Expression or SchemaKind.SensorKey), (place, key) =>
        {
            const string expression = "cpu.load * 2";
            var fraction = Fraction(key);
            switch (key.Kind)
            {
                case SchemaKind.Integer:
                    Fails(place, place.With(key.Name, fraction), $"{key.Name}: {fraction}");
                    break;
                case SchemaKind.Number:
                    Parses(place, place.With(key.Name, fraction), $"{key.Name}: {fraction}");
                    break;
                case SchemaKind.Expression:
                    Parses(place, place.With(key.Name, expression), $"{key.Name}: {expression}");
                    break;
                default:
                    Fails(place, place.With(key.Name, expression), $"{key.Name}: {expression}");
                    break;
            }
        });

    /// A value with a fraction inside the key's range: half past its lowest value, or its middle where that is too far.
    static double Fraction(SchemaKey key)
    {
        var low = key.Min ?? (key.Max is { } top ? Math.Min(0, top - 1) : 0);
        return key.Max is { } max && low + 0.5 > max ? (low + max) / 2 : low + 0.5;
    }

    /// Each value parses, and the parser's own list of choices (from its error for a wrong one) is the catalog's.
    [Fact]
    public void Enums_take_exactly_their_values() =>
        Check(Probes().Where(p => p.Key.Kind is SchemaKind.Enum or SchemaKind.FontWeight), (place, key) =>
        {
            // Another widget type would need other keys, so a discriminator is only tried with its own variant's name.
            foreach (var value in place.Names(key) ? [place.Block.Name] : key.Values)
                Parses(place, place.With(key.Name, value), $"{key.Name}: {value}");
            if (key.Kind == SchemaKind.FontWeight) return;

            var error = Fails(place, place.With(key.Name, "zzz"), $"{key.Name}: zzz");
            var choices = Regex.Match(error.Reason, "one of: (.+); not 'zzz'");
            Assert.True(choices.Success, $"{key.Name}: expected the parser to list its choices, but it said: {error.Reason}");
            var parser = choices.Groups[1].Value.Split(", ");
            Assert.True(parser.SequenceEqual(key.Values),
                $"{place.Block.Id}.{key.Name}: the parser takes {string.Join(", ", parser)}; the catalog lists {string.Join(", ", key.Values)}");
        });

    /// A union's variant rejects the keys only other variants take, such as `fit` on a fill layer or `angle` on a
    /// radial gradient.
    [Fact]
    public void Variants_reject_each_others_keys() =>
        Check(Probes().DistinctBy(p => p.Place.Block.Id).Where(p => p.Place.Union is not null).Select(p => p.Place), place =>
        {
            var union = place.Union!;
            var own = union.KeysOf(place.Block).Select(k => k.Name).ToHashSet();
            foreach (var other in union.Variants.SelectMany(union.KeysOf).DistinctBy(k => k.Name).Where(k => !own.Contains(k.Name)))
                Fails(place, place.With(other.Name, Sample(other)), $"'{other.Name}' on {place.Block.Id}");
        });

    /// A key that belongs to one form of its block, such as an arc shape's `start`, is refused on each other form.
    [Fact]
    public void Keys_of_one_form_are_refused_on_the_others() =>
        Check(Probes().Where(p => p.Key.OnlyFor is not null), (place, key) =>
        {
            var (chooser, form) = key.OnlyFor!.Value;
            var choice = place.Keys.FirstOrDefault(k => k.Name == chooser);
            Assert.True(choice is { Kind: SchemaKind.Enum } && choice.Values.Contains(form),
                $"{place.Block.Id}.{key.Name} is only for {chooser}: {form}, which isn't one of the block's choices");
            foreach (var other in choice!.Values.Where(v => v != form))
            {
                var probe = place.With(key.Name, Sample(key));
                probe[chooser] = other;
                Fails(place, probe, $"'{key.Name}' with {chooser}: {other}");
            }
        });

    /// A key with a lower minimum in one form of its block, such as a line shape's size, takes it in that form only,
    /// and its own minimum in every form.
    [Fact]
    public void Lower_minimums_hold_in_their_form_only() =>
        Check(Probes().Where(p => p.Key.MinFor is not null), (place, key) =>
        {
            var (chooser, form, lower) = key.MinFor!.Value;
            JsonNode Value(double v) => key.Kind == SchemaKind.Size ? new JsonArray(v, 10) : JsonValue.Create(v);
            Assert.True(key.Min is not null, $"{place.Block.Id}.{key.Name} has a lower minimum for {chooser}: {form}, but no Min of its own");

            foreach (var choice in place.Block.Key(chooser)!.Values)
            {
                JsonObject Probe(double value)
                {
                    var probe = place.With(key.Name, Value(value));
                    probe[chooser] = choice;
                    return probe;
                }

                if (choice == form) Parses(place, Probe(lower), $"{key.Name}: {lower} with {chooser}: {choice}");
                else Fails(place, Probe(lower), $"{key.Name}: {lower} with {chooser}: {choice}");
                Parses(place, Probe(key.Min!.Value), $"{key.Name}: {key.Min} with {chooser}: {choice}");
            }
        });

    // ---- Probes: a block's minimal valid mapping, with one key changed, placed in a layout ----

    sealed record Probe(Place Place, SchemaKey Key);

    /// Every key of every block, each where it is allowed.
    static IEnumerable<Probe> Probes()
    {
        foreach (var block in ThemeSchema.Blocks)
            foreach (var key in block.Keys)
                yield return new Probe(new Place(block, null), key);
        foreach (var union in ThemeSchema.Unions)
            foreach (var variant in union.Variants)
                foreach (var key in union.KeysOf(variant))
                    yield return new Probe(new Place(variant, union), key);
    }

    /// Where a block goes in layout.yaml, and the least it needs to parse there.
    sealed record Place(SchemaBlock Block, SchemaUnion? Union)
    {
        public IReadOnlyList<SchemaKey> Keys => Union?.KeysOf(Block) ?? Block.Keys;

        /// Whether `key` is the union's discriminator, which names the variant (a widget's `type`).
        public bool Names(SchemaKey key) => key.Name == Union?.Discriminator;

        /// The required keys, the variant's name and the first of each group the block needs one of; with `forKey`, in
        /// the form that key belongs to (`shape: arc` for an arc's `start`).
        public JsonObject Base(string? forKey = null)
        {
            var probe = new JsonObject();
            foreach (var key in Keys.Where(k => k.Required))
                probe[key.Name] = Names(key) ? Block.Name : Sample(key);
            foreach (var group in new[] { Block.ExactlyOneOf, Block.AtLeastOneOf }.Where(g => g.Count > 0))
                if (!group.Any(probe.ContainsKey))
                    probe[group[0]] = Sample(Keys.First(k => k.Name == group[0]));
            foreach (var (key, value) in Needs(Block.Id))
                probe[key] = value?.DeepClone();
            if (Keys.FirstOrDefault(k => k.Name == forKey)?.OnlyFor is { } form)
                probe[form.Key] = form.Value;
            return probe;
        }

        /// What a block needs to parse in every form, beyond what the catalog can say: a line shape is drawn by its
        /// stroke alone and must have one, and every shape takes one.
        static JsonObject Needs(string id) => id switch
        {
            "widget.shape" => new JsonObject { ["style"] = new JsonObject { ["stroke"] = new JsonObject { ["color"] = "#102030" } } },
            _ => new JsonObject(),
        };

        /// Base with `name` set to `value`, without the others of an exactly-one-of group it belongs to, and with the
        /// room the key needs from its neighbours (RoomFor).
        public JsonObject With(string name, JsonNode? value)
        {
            var probe = Base(name);
            if (Block.ExactlyOneOf.Contains(name))
                foreach (var other in Block.ExactlyOneOf.Where(o => o != name))
                    probe.Remove(other);
            probe[name] = value;
            foreach (var (key, json) in RoomFor.GetValueOrDefault($"{Block.Id}.{name}", []))
                probe[key] = JsonNode.Parse(json);
            return probe;
        }

        public JsonObject With(string name, double value) => With(name, JsonValue.Create(value));

        public JsonObject With(string name, string value) => With(name, (JsonNode)JsonValue.Create(value));

        /// The whole layout.yaml, as JSON, with `probe` where this block goes.
        public string Layout(JsonObject probe)
        {
            var layout = new JsonObject
            {
                ["canvas"] = new JsonObject { ["orientation"] = "landscape" },
                ["styles"] = new JsonObject { [StyleName] = new JsonObject() },
            };
            var id = Union?.Id ?? Block.Id;
            switch (id)
            {
                case "layout":
                    layout = probe;
                    break;
                case "canvas":
                    layout["canvas"] = probe;
                    break;
                case "style" or "style.stroke" or "style.shadow" or "style.glow" or "style.pulse":
                    // A widget uses the style, so a test can read it back.
                    layout["styles"]![ProbeStyle] = id == "style" ? probe : new JsonObject { [Block.Name] = probe };
                    layout["widgets"] = new JsonArray(Widget("text", ("text", "x"), ("style", ProbeStyle)));
                    break;
                case "gradient":
                    layout["background"] = new JsonArray(new JsonObject { ["fill"] = probe });
                    break;
                case "layer":
                    layout["background"] = new JsonArray(probe);
                    break;
                case "widget":
                    layout["widgets"] = new JsonArray(probe);
                    break;
                case "when":
                    layout["widgets"] = new JsonArray(Widget("value", ("source", "cpu.load"), ("when", new JsonArray(probe))));
                    break;
                case "ring":
                    layout["ring"] = probe;
                    break;
                case "ring.when":
                    layout["ring"] = new JsonObject { ["source"] = "cpu.load", ["when"] = new JsonArray(probe) };
                    break;
                case "gauge.needle" or "gauge.ticks":
                    layout["widgets"] = new JsonArray(Widget("gauge", ("source", "cpu.load"), (Block.Name, probe)));
                    break;
                case "graph.grid":
                    layout["widgets"] = new JsonArray(Widget("graph", ("source", "cpu.load"), ("grid", probe)));
                    break;
                case "graph.series":
                    layout["widgets"] = new JsonArray(Widget("graph", ("series", new JsonArray(probe))));
                    break;
                case "donut.slice":
                    // A donut needs two slices; the second is the probe again.
                    layout["widgets"] = new JsonArray(Widget("donut", ("slices", new JsonArray(probe, probe.DeepClone()))));
                    break;
                default:
                    Assert.Fail($"ThemeSchemaDriftTests doesn't know where block '{id}' goes in layout.yaml; add it to Place.Layout");
                    break;
            }

            return layout.ToJsonString();
        }

        static JsonObject Widget(string type, params (string Key, JsonNode Value)[] keys)
        {
            var widget = new JsonObject { ["type"] = type, ["at"] = new JsonArray(0, 0), ["size"] = new JsonArray(10, 10) };
            foreach (var (key, value) in keys) widget[key] = value;
            return widget;
        }
    }

    const string StyleName = "s";
    const string ProbeStyle = "probe";

    /// Keys whose range only fits with room from the keys beside them, by block id and key name, as JSON: the most
    /// segments need a bar longer than a probe's and no gap, since each segment keeps at least 1 px or 1°.
    static readonly Dictionary<string, (string Key, string Json)[]> RoomFor = new(StringComparer.Ordinal)
    {
        ["widget.bar.segments"] = [("size", $"[{SegmentSpec.MaxCount * SegmentSpec.MinLength}, 10]"), ("gap", "0")],
        ["widget.gauge.segments"] = [("gap", "0")],
    };

    static bool IsStyle(SchemaBlock block) => block.Id == "style" || block.Id.StartsWith("style.", StringComparison.Ordinal);

    /// A valid value for the key, wherever it goes.
    static JsonNode Sample(SchemaKey key) => key.Kind switch
    {
        SchemaKind.Number or SchemaKind.Integer => JsonValue.Create(key.Default is { } d
            ? double.Parse(d, CultureInfo.InvariantCulture)
            : key.Min ?? Math.Min(1, key.Max ?? 1)),
        SchemaKind.FontWeight => key.Values[^1],
        SchemaKind.Enum => key.Values[0],
        SchemaKind.Block => new Place(ThemeSchema.Block(key.Of!)!, null).Base(),
        SchemaKind.List when key.ItemKind is { } item =>
            new JsonArray(Enumerable.Range(0, key.MinItems ?? 1).Select(_ => (JsonNode?)Sample(item)).ToArray()),
        // A list of blocks that must have entries, such as a graph's series, gets as many of the least each needs.
        SchemaKind.List when key is { Of: { } of, MinItems: > 0 } && ThemeSchema.Block(of) is { } entry =>
            new JsonArray(Enumerable.Range(0, key.MinItems.Value).Select(_ => (JsonNode?)new Place(entry, null).Base()).ToArray()),
        SchemaKind.List => new JsonArray(),
        SchemaKind.Map => new JsonObject(),
        SchemaKind.Uniforms => new JsonObject { ["u"] = "cpu.load" },
        SchemaKind.ThemeFile => "a" + (key.FileTypes.FirstOrDefault() ?? ".png"),
        var kind => Sample(kind),
    };

    static JsonNode Sample(SchemaKind kind) => kind switch
    {
        SchemaKind.Boolean => true,
        SchemaKind.String or SchemaKind.Template => "x",
        SchemaKind.Color or SchemaKind.Paint => "#102030",
        SchemaKind.FontFamily => "Inter",
        SchemaKind.Expression or SchemaKind.SensorKey => "cpu.load",
        SchemaKind.Format => "{0}",
        SchemaKind.Point => new JsonArray(0, 0),
        SchemaKind.Size => new JsonArray(10, 10),
        SchemaKind.StyleName => StyleName,
        SchemaKind.Style => new JsonObject(),
        _ => throw new InvalidOperationException($"ThemeSchemaDriftTests has no sample {kind}; add one to Sample"),
    };

    /// The key's default as layout.yaml would write it.
    static JsonNode Default(SchemaKey key) => key.Kind switch
    {
        SchemaKind.Number or SchemaKind.Integer => JsonValue.Create(double.Parse(key.Default!, CultureInfo.InvariantCulture)),
        SchemaKind.Boolean => JsonValue.Create(key.Default == "true"),
        _ => JsonValue.Create(key.Default!),
    };

    /// `count` entries for a list, map or shader's uniforms, each the least that parses there.
    static JsonNode Items(SchemaKey key, int count)
    {
        var indexes = Enumerable.Range(0, count).ToList();
        return key.Kind switch
        {
            SchemaKind.Uniforms => new JsonObject(indexes.Select(i => KeyValuePair.Create($"u{i}", (JsonNode?)"cpu.load"))),
            SchemaKind.Map => new JsonObject(indexes.Select(i => KeyValuePair.Create($"m{i}", (JsonNode?)Entry(key)))),
            _ => new JsonArray(indexes.Select(_ => (JsonNode?)Entry(key)).ToArray()),
        };
    }

    /// One entry of a list or map: a value of its item kind, or the least its block, or its union's first variant, needs.
    static JsonNode Entry(SchemaKey key) =>
        key.ItemKind is { } item ? Sample(item)
        : ThemeSchema.Union(key.Of!) is { } union ? new Place(union.Variants[0], union).Base()
        : new Place(ThemeSchema.Block(key.Of!)!, null).Base();

    static void Parses(Place place, JsonObject probe, string what)
    {
        var yaml = place.Layout(probe);
        var error = Record.Exception(() => Layouts.Parse(yaml));
        Assert.True(error is null, $"{place.Block.Id}: expected {what} to parse, but: {error?.Message}\n{yaml}");
    }

    static ThemeException Fails(Place place, JsonObject probe, string what)
    {
        var yaml = place.Layout(probe);
        var error = Record.Exception(() => Layouts.Parse(yaml));
        Assert.True(error is ThemeException, $"{place.Block.Id}: expected {what} to be an error, but it parsed\n{yaml}");
        return (ThemeException)error!;
    }

    /// Runs `check` for each probe and reports every failure at once, so one run lists all the drift.
    static void Check(IEnumerable<Probe> probes, Action<Place, SchemaKey> check) =>
        Check(probes, probe => check(probe.Place, probe.Key));

    static void Check<T>(IEnumerable<T> items, Action<T> check)
    {
        var failures = new List<string>();
        foreach (var item in items)
        {
            try
            {
                check(item);
            }
            catch (Exception ex)
            {
                failures.Add(ex is Xunit.Sdk.XunitException ? ex.Message : ex.ToString());
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }
}
