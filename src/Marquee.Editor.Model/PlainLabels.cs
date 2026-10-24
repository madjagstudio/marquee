using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

public enum PlainSection { Basics, Text, FillAndBorder, Effects, Position, Motion, WhatItShows }

// Slider bounds are in schema units; Minimum is a UI floor, never a validation rule for typed values.
// A size's catalog maximum is capped to the current canvas axis by the property editor.
public sealed record PlainLabel(string Label, PlainSection Section, string Tooltip, IReadOnlySet<string> BasicFor,
    double? SliderMin = null, double? SliderMax = null, double? Minimum = null);

/// Plain words for the schema, shared by every properties panel.
public static class PlainLabels
{
    static readonly FrozenDictionary<(string Block, string Key), Words> Definitions = Define();
    static readonly Lazy<FrozenDictionary<(string Owner, string Path), PlainLabel>> Catalog = new(Build);
    static readonly Regex ItemIndex = new(@"\[[0-9]+\]", RegexOptions.CultureInvariant);
    static readonly IReadOnlySet<string> NoBasics = Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal);

    /// Owners are widget types, layer.* kinds, canvas, style, when, ring and ring.when.
    /// Paths follow PropertyRow.Name, including canvas.orientation and a named style's style.font (but bare extends).
    public static PlainLabel? For(string ownerKind, string keyPath) =>
        Catalog.Value.GetValueOrDefault((ownerKind, ItemIndex.Replace(keyPath, "[0]")));

    /// Walk the schema, independently of the words table, so a new key cannot silently evade coverage.
    /// Lists use one representative item, [0]; For accepts any nonnegative item index.
    /// Group children are separate widget owners, not an infinitely recursive property path.
    public static IEnumerable<(string Owner, string KeyPath)> AllKeys() =>
        Walk().Select(entry => (entry.Owner, entry.Path));

    sealed record Words(string Label, PlainSection Section, string Tooltip, double? SliderMin, double? SliderMax, double? Minimum);
    sealed record Entry(string Owner, string Path, string Block, SchemaKey Key, string Prefix);

    static FrozenDictionary<(string Owner, string Path), PlainLabel> Build()
    {
        var result = new Dictionary<(string, string), PlainLabel>();
        foreach (var entry in Walk())
        {
            if (!Definitions.TryGetValue((entry.Block, entry.Key.Name), out var words)) continue;
            var basic = words.Section is PlainSection.Basics or PlainSection.WhatItShows || IsBasic(entry.Owner, entry.Path);
            result.Add((entry.Owner, entry.Path), new(entry.Prefix + words.Label, words.Section, words.Tooltip,
                basic ? new[] { entry.Owner }.ToFrozenSet(StringComparer.Ordinal) : NoBasics,
                words.SliderMin, words.SliderMax, words.Minimum));
        }
        return result.ToFrozenDictionary();
    }

    static bool IsBasic(string owner, string path) => owner switch
    {
        "style" => path is "style.color" or "style.fill" or "style.font",
        "when" => path == "style",
        "ring" => path is "color" or "effect" or "when",
        "ring.when" => path is "color" or "effect",
        "layer.fill" => path == "fill",
        "layer.image" => path == "image",
        "layer.video" => path == "video",
        "layer.shader" => path == "shader",
        "canvas" => path == "canvas.orientation",
        "text" or "value" or "bar" or "gauge" or "graph" or "donut" =>
            path is "size" or "when" or "style.color" || (owner, path) is
                ("gauge", "needle" or "start" or "sweep" or "thickness") or ("donut", "hole") or
                ("bar", "direction") or ("graph", "kind"),
        "shape" => path is "size" or "when" or "style.fill" or "shape",
        "image" or "video" or "group" => path is "size" or "when" or "style.fill",
        _ => false,
    };

    static IEnumerable<Entry> Walk()
    {
        foreach (var variant in ThemeSchema.Widgets.Variants)
        {
            if (ThemeSchema.Widgets.DiscriminatorKey is { } type)
                foreach (var entry in WalkKey(variant.Name, "widget", type, type.Name)) yield return entry;
            foreach (var key in ThemeSchema.WidgetKeys(variant.Name).Where(key => key.Name != ThemeSchema.Widgets.Discriminator))
            {
                var block = variant.Key(key.Name) is null ? ThemeSchema.Widgets.Common.Id : variant.Id;
                foreach (var entry in WalkKey(variant.Name, block, key, key.Name)) yield return entry;
            }
        }
        foreach (var variant in ThemeSchema.Background.Variants)
            foreach (var key in ThemeSchema.Background.KeysOf(variant))
            {
                var block = variant.Key(key.Name) is null ? ThemeSchema.Background.Common.Id : variant.Id;
                foreach (var entry in WalkKey(variant.Id, block, key, key.Name)) yield return entry;
            }
        foreach (var block in new[] { ThemeSchema.Canvas, ThemeSchema.Style, ThemeSchema.When, ThemeSchema.Ring, ThemeSchema.RingWhen })
            foreach (var key in block.Keys)
            {
                var prefix = block.Id == "canvas" ? "canvas." : block.Id == "style" && key.Name != "extends" ? "style." : "";
                foreach (var entry in WalkKey(block.Id, block.Id, key, prefix + key.Name)) yield return entry;
            }
    }

    static IEnumerable<Entry> WalkKey(string owner, string block, SchemaKey key, string path, string prefix = "")
    {
        yield return new(owner, path, block, key, prefix);
        // The definitions affect only wording, never which schema keys are visited.
        var label = Definitions.GetValueOrDefault((block, key.Name))?.Label;
        if (key.Kind == SchemaKind.Style)
        {
            foreach (var child in ThemeSchema.Style.Keys.Where(child => key.StyleKeys.Count == 0 || key.StyleKeys.Contains(child.Name)))
                foreach (var entry in WalkKey(owner, "style", child, $"{path}.{child.Name}", prefix)) yield return entry;
        }
        else if (key.Kind == SchemaKind.Paint)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var variant in ThemeSchema.Gradient.Variants)
                foreach (var child in ThemeSchema.Gradient.KeysOf(variant))
                {
                    if (!seen.Add(child.Name)) continue;
                    var childBlock = variant.Key(child.Name) is null ? ThemeSchema.Gradient.Common.Id : variant.Id;
                    foreach (var entry in WalkKey(owner, childBlock, child, $"{path}.{child.Name}", $"{prefix}{label}: ")) yield return entry;
                }
        }
        else if (key.Of is { } of && of != ThemeSchema.Widgets.Id)
        {
            var childPath = key.Kind == SchemaKind.List ? path + "[0]" : path;
            var childPrefix = key.Kind == SchemaKind.List ? $"{prefix}{label}: " : prefix;
            if (ThemeSchema.Block(of) is { } children)
                foreach (var child in children.Keys)
                    foreach (var entry in WalkKey(owner, of, child, $"{childPath}.{child.Name}", childPrefix)) yield return entry;
            else if (ThemeSchema.Union(of) is { } union)
                foreach (var childBlock in union.Variants.Prepend(union.Common))
                    foreach (var child in childBlock.Keys)
                        foreach (var entry in WalkKey(owner, childBlock.Id, child, $"{childPath}.{child.Name}", childPrefix)) yield return entry;
        }
    }

    // Each entry is deliberately authored against a declaring schema block, not guessed from a key name or its help.
    // Shared entries list their blocks explicitly; new keys (including new keys inside existing blocks) need words here.
    static FrozenDictionary<(string Block, string Key), Words> Define()
    {
        var words = new Dictionary<(string, string), Words>();
        void Add(string blocks, string key, string label, PlainSection section, string tooltip,
            double? sliderMin = null, double? sliderMax = null, double? minimum = null)
        {
            foreach (var block in blocks.Split(' ')) words.Add((block, key), new(label, section, tooltip, sliderMin, sliderMax, minimum));
        }

        const PlainSection basics = PlainSection.Basics, shows = PlainSection.WhatItShows, text = PlainSection.Text,
            fill = PlainSection.FillAndBorder, effects = PlainSection.Effects, position = PlainSection.Position, motion = PlainSection.Motion;

        Add("widget", "type", "Widget type", position, "Identifies the kind of widget on the canvas.");
        Add("widget.common", "at", "Position", position, "Places the top-left corner within the canvas or its group.", -1920, 1920);
        Add("widget.common widget.shape", "size", "Size", position, "Sets the width and height of the widget's box.", 8, 1920);
        Add("widget.common widget.value widget.bar widget.gauge", "source", "What it shows", shows, "Chooses a sensor value or calculation to display and compare in warnings.");
        Add("widget.graph", "source", "What it shows", shows, "Chooses one sensor whose recent readings appear on the graph.");
        Add("widget.common", "style", "Style", fill, "Chooses a saved style or sets this widget's appearance here.");
        Add("widget.common", "when", "Warnings", basics, "Changes the appearance while a condition matches, with later matching rules taking priority.");
        Add("widget.common", "smooth", "Smooth movement", motion, "Sets how long a value, bar or gauge takes to reach a new reading, with zero changing immediately.", 0, 2000);
        Add("widget.common", "rotate", "Rotation", position, "Turns the widget clockwise around its centre, including any children.", -180, 180);
        Add("widget.common layer.common", "hidden", "Hide", position, "Keeps this item off the screen without removing it from the theme.");
        Add("widget.common", "locked", "Lock", position, "Prevents moving or resizing this widget with the mouse.");
        Add("layer.common", "locked", "Lock", position, "Prevents editing this background layer.");
        Add("widget.common layer.common", "name", "Name in layers", position, "Sets the name shown for this item in the editor's layers list.");

        Add("widget.text", "text", "Text", shows, "Sets the words to show, with placeholders for live readings or the clock.");
        Add("widget.text widget.value", "spans", "Styled text parts", shows, "Splits text into parts that can each have their own lettering style.");
        Add("text.span", "text", "Text", shows, "Sets this part's words or value placeholders, with line breaks starting new lines.");
        Add("text.span", "style", "Letter style", text, "Changes the lettering of this part while the widget controls its layout.");
        Add("widget.value", "format", "Value format", shows, "Controls how the value is written, including decimals, units, time or duration.");
        Add("widget.bar widget.gauge", "min", "Empty at", shows, "Sets the reading at which the meter is empty.", 0, 100);
        Add("widget.bar widget.gauge", "max", "Full at", shows, "Sets the reading at which the meter is full, above its empty value.", 1, 200);
        Add("widget.bar", "direction", "Fill direction", fill, "Fills the bar from left to right or from bottom to top.");
        Add("widget.bar widget.gauge", "segments", "Segments", fill, "Divides the meter into equal pieces that light up as its value rises.", 2, 60);
        Add("widget.bar widget.gauge", "gap", "Segment spacing", fill, "Sets the gap between the meter's segments.", 0, 20);
        Add("widget.gauge widget.shape", "start", "Arc start", fill, "Sets where the arc begins, measured clockwise from three o'clock.", -180, 180);
        Add("widget.gauge widget.shape", "sweep", "Arc length", fill, "Sets how many degrees the arc covers, with negative values running anticlockwise.", -270, 270);
        Add("widget.gauge", "thickness", "Arc thickness", fill, "Sets the arc's width, with zero leaving only the dial's needle and ticks.", 0, 40);
        Add("widget.shape", "thickness", "Arc thickness", fill, "Sets the width of the arc's band.", 1, 40);
        Add("widget.gauge", "needle", "Dial needle", fill, "Adds a needle that points to the current value.");
        Add("widget.gauge", "ticks", "Tick marks", fill, "Adds scale marks inside the arc, with optional value labels.");
        Add("gauge.needle", "width", "Needle width", fill, "Sets the needle's width at its base before it narrows to a point.", 1, 20);
        Add("gauge.needle", "length", "Needle length", fill, "Sets how far the needle reaches as a share of the dial's radius.", 0.1, 1);
        Add("gauge.needle", "color", "Needle colour", fill, "Colours the needle and its centre hub, overriding the main colour.");
        Add("gauge.needle", "cap", "Needle hub size", fill, "Sets the radius of the circle covering the needle's base, with zero hiding it.", 0, 20);
        Add("gauge.ticks", "major", "Scale divisions", fill, "Sets how many equal intervals the main tick marks divide the scale into.", 1, 20);
        Add("gauge.ticks", "minor", "Small ticks per division", fill, "Adds this many shorter marks between neighbouring main ticks.", 0, 10);
        Add("gauge.ticks", "length", "Tick length", fill, "Sets how far the main tick marks reach inward from the arc.", 1, 30);
        Add("gauge.ticks", "width", "Tick thickness", fill, "Sets the width of each tick mark.", 1, 8);
        Add("gauge.ticks", "color", "Tick colour", fill, "Colours the tick marks, overriding the main colour.");
        Add("gauge.ticks", "labels", "Tick value format", text, "Formats the values beside the main ticks, or hides them when turned off.");

        Add("widget.graph", "series", "Sensor series", shows, "Plots several sensors together, each with its own colour and line style.");
        Add("widget.graph", "kind", "Graph form", fill, "Shows readings as lines, filled areas or columns.");
        Add("widget.graph", "min", "Scale bottom", shows, "Sets the bottom of the graph's scale, or leaves it to fit the readings.", 0, 100);
        Add("widget.graph", "max", "Scale top", shows, "Sets the top of the graph's scale, or leaves it to fit the readings.", 1, 200);
        Add("widget.graph", "points", "History length", shows, "Sets how many recent sensor readings fit across the graph.", 2, 200);
        Add("widget.graph", "grid", "Grid lines", fill, "Adds evenly spaced horizontal lines behind the readings.");
        Add("graph.grid", "lines", "Grid line count", fill, "Sets how many horizontal lines divide the graph's height.", 1, 20);
        Add("graph.grid", "color", "Grid colour", fill, "Sets the colour of the graph's horizontal lines.");
        Add("graph.grid", "width", "Grid line thickness", fill, "Sets the width of the grid lines, with zero hiding them.", 0, 8);
        Add("graph.series", "source", "What it shows", shows, "Chooses the sensor whose history this series plots.");
        Add("graph.series", "style", "Series style", fill, "Changes this series' appearance over the graph's shared style.");

        Add("widget.donut", "hole", "Centre hole", fill, "Sets the hole as a share of the radius, with zero making a pie.", 0, 0.9);
        Add("widget.donut", "start", "First slice angle", fill, "Places the first slice clockwise from three o'clock, with minus ninety at the top.", -180, 180);
        Add("widget.donut", "gap", "Slice spacing", fill, "Sets the angular gap between neighbouring slices.", 0, 30);
        Add("widget.donut", "labels", "Slice label position", text, "Places slice labels inside or outside the ring, or hides them.");
        Add("widget.donut", "format", "Slice percentage format", shows, "Formats each slice's percentage after its label.");
        Add("widget.donut", "slices", "Slices", shows, "Sets the parts of the whole, arranged clockwise.");
        Add("donut.slice", "source", "What it shows", shows, "Chooses the slice's value, with zero or negative values taking no space.");
        Add("donut.slice", "label", "Label text", shows, "Sets the words that identify this slice.");
        Add("donut.slice", "style", "Slice style", fill, "Changes this slice's fill, border, glow or shadow over the donut's style.");
        Add("widget.image", "src", "Image file", shows, "Chooses a picture from the theme folder.");
        Add("widget.video", "src", "Video file", shows, "Chooses a clip from the theme folder to play repeatedly.");
        Add("widget.image widget.video layer.image layer.video", "fit", "Fit in box", position, "Chooses whether to crop, fit inside, stretch or keep the picture's original size.");
        Add("widget.shape", "shape", "Shape form", fill, "Chooses a rectangle, ellipse, line or arc.");
        Add("widget.group", "children", "Widgets in group", position, "Holds the widgets drawn inside this group, in drawing order.");

        Add("layer.common style", "opacity", "Transparency", effects, "Sets how solid the item looks, shown as 0–100% opaque.", 0, 1);
        Add("layer.common style", "blend", "Blend with what's behind", effects, "Chooses how this item's colours mix with the content beneath it.");
        Add("layer.fill", "fill", "Background fill", fill, "Paints the canvas with a colour or gradient.");
        Add("layer.image", "image", "Background image", shows, "Chooses a picture from the theme folder to cover the canvas.");
        Add("layer.video", "video", "Background video", shows, "Chooses a clip from the theme folder to loop behind the widgets.");
        Add("layer.shader", "shader", "Background shader", shows, "Chooses a shader file that paints an animated background.");
        Add("layer.shader", "uniforms", "Shader inputs", shows, "Feeds numbers or sensor calculations into the shader's custom inputs.");
        Add("layer.shader", "scale", "Shader resolution", effects, "Draws the shader at a smaller size and enlarges it to reduce rendering work.", 0.1, 1);
        Add("canvas", "orientation", "Screen orientation", position, "Chooses a wide landscape canvas or a tall portrait canvas.");
        Add("canvas", "fps", "Frames per second", motion, "Sets how many frames the theme draws each second.", 1, 60);

        Add("style", "extends", "Base style", fill, "Starts from another named style and replaces the settings changed here.");
        Add("style", "font", "Font", text, "Chooses an installed font or a font file in the theme folder.");
        Add("style", "size", "Text size", text, "Sets the height of the lettering in pixels.", 8, 120, minimum: 6);
        Add("style", "weight", "Text weight", text, "Makes the lettering lighter or bolder.", 300, 700);
        Add("style", "color", "Colour", fill, "Sets the main colour for lettering, meters and any parts without their own colour.");
        Add("style", "fill", "Fill", fill, "Paints the box, shape, filled meter, slices or graph area with a colour or gradient.");
        Add("style", "track", "Track colour", fill, "Paints the empty meter, graph background or ring behind the slices.");
        Add("style", "stroke", "Border", fill, "Sets the outline or graph line, including the ends of lines and arcs.");
        Add("style", "shadow", "Shadow", effects, "Adds an offset shadow beneath the item.");
        Add("style", "glow", "Glow", effects, "Adds a soft halo around letters, shapes or slices.");
        Add("style", "pulse", "Pulse", effects, "Makes transparency or glow rise and fall in a steady beat.");
        Add("style", "radius", "Corner rounding", fill, "Rounds the corners of boxes, bars and segments.", 0, 40);
        Add("style", "padding", "Text inset", text, "Adds space between the lettering and the edge of its box.", 0, 40);
        Add("style", "align", "Text alignment", text, "Places each line at the left, centre or right of its box.");
        Add("style", "letter_spacing", "Letter spacing", text, "Adds space between letters, with negative values moving them closer.", -5, 20);
        Add("style", "line_height", "Line spacing", text, "Sets the distance between lines as a multiple of the text size.", 0.8, 2);
        Add("style", "wrap", "Wrap text", text, "Moves words onto more lines when they are too wide for the box.");
        Add("style", "text_stroke", "Letter outline", text, "Draws a border behind the edges of each letter.");
        Add("style", "text_fill", "Letter fill", text, "Paints letters with a colour or gradient in place of the main colour.");
        Add("style", "italic", "Italic", text, "Slants the lettering using the font's italic face when available.");
        Add("style", "underline", "Underline", text, "Draws a line beneath the text in its lettering colour.");
        Add("style", "strike", "Strikethrough", text, "Draws a line through the middle of the text.");
        Add("style", "case", "Capitalisation", text, "Changes the displayed text to upper case, lower case or initial capitals.");
        Add("style.stroke", "width", "Border width", fill, "Sets the outline or graph line thickness, with zero hiding it.", 0, 12);
        Add("style.stroke", "color", "Border colour", fill, "Paints the outline or graph line with a colour or gradient.");
        Add("style.stroke", "cap", "Line ends", fill, "Makes the ends of lines and arcs flat, round or square.");
        Add("style.shadow", "color", "Shadow colour", effects, "Colours the shadow and enables it when a colour is set.");
        Add("style.shadow", "x", "Shadow horizontal offset", effects, "Moves the shadow right, or left with a negative value.", -30, 30);
        Add("style.shadow", "y", "Shadow vertical offset", effects, "Moves the shadow down, or up with a negative value.", -30, 30);
        Add("style.shadow", "blur", "Shadow softness", effects, "Softens the shadow's edges, with zero keeping them sharp.", 0, 30);
        Add("style.glow", "color", "Glow colour", effects, "Colours the halo and enables it when a colour is set.");
        Add("style.glow", "radius", "Glow spread", effects, "Sets how far the soft halo reaches beyond the item.", 0, 30);
        Add("style.text_stroke", "width", "Letter outline width", text, "Sets how far the outline extends beyond each letter, with zero hiding it.", 0, 8);
        Add("style.text_stroke", "color", "Letter outline colour", text, "Paints the letter outlines with a colour or gradient.");
        Add("style.pulse", "property", "Pulse target", effects, "Chooses whether the beat changes transparency or glow spread.");
        Add("style.pulse", "period", "Pulse duration", effects, "Sets how long one complete beat takes.", 50, 5000);
        Add("style.pulse", "min", "Pulse low point", effects, "Sets the weakest part of the beat as a share of normal opacity or glow.", 0, 1);
        Add("style.pulse", "max", "Pulse high point", effects, "Sets the strongest part of the beat as a share of normal opacity or glow.", 0, 1);

        Add("gradient.common", "stops", "Colour positions", fill, "Places each gradient colour between the start and end, or spreads them evenly when left unset.", 0, 1);
        Add("gradient.linear", "linear", "Linear colours", fill, "Sets the colours in order along a straight gradient.");
        Add("gradient.linear", "angle", "Gradient direction", fill, "Turns the gradient clockwise, with zero running left to right.", -180, 180);
        Add("gradient.radial", "radial", "Radial colours", fill, "Sets the colours in order from the centre of a circular gradient outward.");
        Add("gradient.radial", "center", "Gradient centre", fill, "Places the circular gradient's centre using horizontal and vertical fractions of the painted area.", 0, 1);
        Add("gradient.radial", "radius", "Gradient reach", fill, "Sets how far the circular gradient spreads relative to half the area's longer side.", 0, 3);

        Add("when ring.when", "above", "Above", shows, "Applies this rule while the source value is greater than this number.", 0, 100);
        Add("when ring.when", "below", "Below", shows, "Applies this rule while the source value is less than this number.", 0, 100);
        Add("when ring.when", "equals", "Equals", shows, "Applies this rule while the source matches this number or text.");
        Add("when", "stale", "Reading unavailable", shows, "Applies this rule when the source, or a reading in the text, has no fresh value.");
        Add("ring.when", "stale", "Reading unavailable", shows, "Applies this rule while the ring's source has no fresh value.");
        Add("when", "style", "Appearance change", fill, "Applies this style while the rule's condition matches.");
        Add("ring ring.when", "color", "Ring colour", fill, "Sets the LEDs' colour, with colour transparency dimming the light.");
        Add("ring ring.when", "effect", "Ring effect", effects, "Chooses steady light, breathing, a spinning segment or a moving rainbow.");
        Add("ring ring.when", "speed", "Effect speed", motion, "Sets how fast the ring effect runs, with two running twice as fast.", 0.1, 5);
        Add("ring ring.when", "brightness", "Ring brightness", effects, "Sets how brightly the ring's LEDs shine.", 0, 100);
        Add("ring", "source", "What it shows", shows, "Chooses the sensor value or calculation that the ring's warnings compare.");
        Add("ring", "when", "Warnings", basics, "Changes the ring while conditions match, with later rules taking priority.");
        return words.ToFrozenDictionary();
    }
}
