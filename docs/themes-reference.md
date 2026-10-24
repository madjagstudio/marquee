# Theme reference

<!-- Generated from the theme schema catalog (src/Marquee.Core/Themes/Schema) by tools/Marquee.SchemaDocs.
     Don't edit it by hand: change the catalog, then run `dotnet run --project tools/Marquee.SchemaDocs`. -->

Every key a theme's `layout.yaml` can have: what it takes, what it is when it's left out, and what it does.
Marquee's theme catalog generates this page, and [`layout.schema.json`](layout.schema.json) too, which lets
VS Code's YAML extension complete and check theme files.

- **Unknown keys are errors**, so a typo never silently does nothing. `marquee validate` lists every mistake,
  with its line.
- A **required** key must be there. A **default** is what a key is when it's left out; a key without one does
  nothing until it is set, unless its description says otherwise.
- **Kinds of value**, such as colors, paints and expressions, are explained at the end.

## layout.yaml

The theme's main file. It needs a `canvas`; everything else is optional.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `canvas` | block: [Canvas](#canvas) |  | required | The screen the theme is made for: which way round, and how many frames a second. |
| `styles` | named [Styles](#styles) |  |  | Named styles, which widgets and `when` rules use by name. |
| `background` | list of [Background layers](#background-layers) | up to 16 |  | Layers drawn behind the widgets, first at the bottom: up to 16, at most 4 of them shaders and 2 videos. |
| `widgets` | list of [Widgets](#widgets) |  |  | What's drawn on the screen, in order, so later widgets cover earlier ones. Up to 2000 in all, counting the ones in groups. |
| `ring` | block: [LED ring](#led-ring) |  |  | What the screen's LED ring shows. Left out, the ring is a soft warm white at 40%. |

## Canvas

Goes under `canvas:` in [layout.yaml](#layoutyaml).

The screen the theme is made for. A theme fits both ways up of its orientation.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `orientation` | choice | `portrait`, `landscape` | required | How the screen is mounted: landscape is 1920×480, portrait 480×1920. |
| `fps` | whole number | 1 to 60 | `30` | Frames drawn each second. |

## Styles

Goes under `styles:` in [layout.yaml](#layoutyaml).

How a widget looks. A style is named in `styles:`, or written in place as a widget's or rule's `style:`. A group passes its text keys (font, size, weight, color, align, letter_spacing, line_height, wrap) and its letter effects (text_stroke, text_fill, italic, underline, strike, case) down to its children; the others stay with its box.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `extends` | style name |  |  | Another named style to start from; keys set here override it. |
| `font` | font |  | `Inter` | A font installed on this PC, by name, or a .ttf, .otf or .ttc file in the theme folder. Inter is built in. Characters the font lacks are drawn with a Windows font that has them: the right letters, but Arabic letters aren't joined yet, Arabic and Hebrew run left to right, and Indic marks aren't yet moved into place. Color emoji keep their own colors, and take no glow, shadow or outline. |
| `size` | number (px) | 1 to 1000 | `24` | Text size. |
| `weight` | font weight | 100 to 900, or `light`, `normal`, `medium`, `semibold`, `bold` | `normal` | How heavy text is: 100 to 900, or a name (light 300 to bold 700). |
| `color` | color |  | `#FFFFFF` | The text's color, and the filled part of a bar, gauge or graph line when `fill` or `stroke` doesn't set one. A dial's needle and ticks, and a donut's slices, use it when they set no color of their own. |
| `fill` | paint |  |  | The background of a text, value, image or group box, and the inside of a shape. On a bar or gauge, the filled part or the lit segments, and on a donut, each slice (the style's color when left out); on a graph, the area under the line or the columns (an area or columns graph uses the color without it). |
| `track` | paint |  |  | The empty part of a bar or gauge, or its unlit segments, the background behind a graph, and the ring behind a donut's slices. |
| `stroke` | block: [Stroke](#stroke) |  |  | An outline round a box, a bar, a shape or a donut's slice (a line shape is the stroke alone), a graph's line, and the ends of a gauge's arc, an arc shape or a line (its cap). |
| `shadow` | block: [Shadow](#shadow) |  |  | A shadow under text, a shape, a gauge's needle, a donut's slices, or an image or video widget's box. |
| `glow` | block: [Glow](#glow) |  |  | A soft glow round text, a shape or a donut's slices. |
| `pulse` | block: [Pulse](#pulse) |  |  | Makes the opacity or the glow beat steadily. |
| `opacity` | number (fraction) | 0 to 1 | `1` | How solid the whole widget is: 0 hides it, 1 is fully solid. |
| `radius` | number (px) | at least 0 | `0` | Rounds the corners of a box, a bar, the segments of a bar or gauge, a rect shape, a graph's background and what a group or image clips to. |
| `padding` | number (px) | at least 0 | `0` | Space between a text or value widget's box and its text. |
| `align` | choice | `left`, `center`, `right` | `left` | Where each line of text sits across its box. The lines together are always centred from top to bottom. |
| `blend` | choice | `normal`, `multiply`, `screen`, `overlay`, `darken`, `lighten`, `add` | `normal` | How the whole widget mixes with what's under it, as a background layer's blend does. A group's children blend with it, as one. Inside a group with an opacity below 1 or a blend of its own, a widget's blend mixes with the group's layer, not with what's behind the group. |
| `letter_spacing` | number (px) | -20 to 100 | `0` | Space added between the characters of text; below 0 draws them closer. |
| `line_height` | number (times) | 0.5 to 4 | `1.2` | How far apart the lines of text are, as a multiple of the text's size. |
| `wrap` | true or false |  | `false` | Breaks text too wide for its box onto more lines, at spaces. Without it, such text draws on one line past its box. A line break starts a new line either way. |
| `text_stroke` | block: [Text stroke](#text-stroke) |  |  | An outline round each letter, drawn behind the letters' fill. |
| `text_fill` | paint |  |  | The letters' color or gradient, in place of `color`, which keeps its other jobs. A span's or a rule's `color` doesn't change letters a `text_fill` paints: set `text_fill` there instead. A gradient spans the widget's box, so texts whose boxes line up share one. |
| `italic` | true or false |  | `false` | Slanted letters: the font's italic face when an installed font has one, otherwise the letters slanted. |
| `underline` | true or false |  | `false` | A line under the text, as long as the text, in the letters' color. |
| `strike` | true or false |  | `false` | A line through the text, as long as the text, in the letters' color. |
| `case` | choice | `upper`, `lower`, `title`, `none` | `none` | Changes the text's case once its placeholders are filled in, by the rules of the PC's regional format: `title` capitalises the first letter of every word, am and pm included, and leaves the rest as written. |

### Stroke

Goes under `stroke:` in [Styles](#styles).

A line: the outline drawn just inside a box or a shape and round a donut's slice, a line shape itself, a graph's line, or the ends of a gauge's arc or an arc shape.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `width` | number (px) | at least 0 | `1`; `2` on a graph | How thick the line is; 0 draws none. |
| `color` | paint |  |  | The line's color or gradient. A box has no outline without it; a graph's line uses the style's color. |
| `cap` | choice | `butt`, `round`, `square` | `butt`; `round` on a graph | How the ends of a gauge's arc, a graph's line, and a line or arc shape are drawn. It doesn't shape a segmented gauge, whose segments `radius` rounds. |

### Shadow

Goes under `shadow:` in [Styles](#styles).

A copy of the text, a shape, a gauge's needle or a donut's slices, or of an image or video widget's box, drawn under it, offset and softened.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `color` | color |  |  | The shadow's color. There is no shadow without it. |
| `x` | number (px) |  | `2` | How far right the shadow sits; negative moves it left. |
| `y` | number (px) |  | `2` | How far down the shadow sits; negative moves it up. |
| `blur` | number (px) | at least 0 | `0` | How soft the shadow is; 0 is sharp. |

### Glow

Goes under `glow:` in [Styles](#styles).

A blurred copy of the text, a shape or a donut's slices, drawn behind it.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `color` | color |  |  | The glow's color. There is no glow without it. |
| `radius` | number (px) | at least 0 | `8` | How far the glow spreads. |

### Pulse

Goes under `pulse:` in [Styles](#styles).

A steady beat: the opacity or the glow eases from max down to min and back, once each period.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `property` | choice | `opacity`, `glow` | `opacity` | What beats: the widget's opacity, or its glow's radius. |
| `period` | number (ms) | at least 50 | `1000` | How long one beat takes. |
| `min` | number (fraction) | 0 to 1 | `0.4` | The low point, as a fraction of the normal opacity or glow. |
| `max` | number (fraction) | 0 to 1 | `1` | The high point, as a fraction of the normal opacity or glow. |

### Text stroke

Goes under `text_stroke:` in [Styles](#styles).

An outline round each letter, and round the lines under and through them. It is drawn behind the letters' fill, so they keep their shape and the outline shows `width` px outside them, with rounded corners.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `width` | number (px) | at least 0 | `1` | How far the outline shows outside the letters; 0 draws none. |
| `color` | paint |  |  | The outline's color or gradient, which spans the widget's box. There is no outline without it. |

## Gradients

A paint is a color or a gradient. A gradient is a mapping with `linear:` or `radial:` and two or more colors, and goes wherever a key takes a paint: `fill`, `track`, a stroke's `color` or a fill layer.

### Every gradient

Both kinds of gradient take these.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `stops` | list of numbers (fraction) | at least 2, each 0 to 1 |  | Where each color sits, from 0 (the line's start, or the centre) to 1 (the line's end, or the radius): one per color, each no lower than the one before. Two equal stops make a hard edge. Left out, the colors spread evenly. |

### Linear gradient

Colors spread in a straight line across the area.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `linear` | list of colors | at least 2 | required | The colors, from the start of the line to its end. |
| `angle` | number (degrees) |  | `0` | The line's direction, clockwise: 0 runs left to right, 90 top to bottom. |

### Radial gradient

Colors spread in a circle from a centre point outwards.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `radial` | list of colors | at least 2 | required | The colors, from the centre out to the radius. |
| `center` | list of numbers (fraction) | 2, each 0 to 1 | `[0.5, 0.5]` | Where the circle starts, as [x, y] fractions of the area: [0, 0] is its top-left corner. |
| `radius` | number (fraction) | 0 to 3 | `1` | How far the colors reach, as a fraction of half the area's longer side. 3 covers the whole area from any centre, even a corner. |

## Background layers

Goes under `background:` in [layout.yaml](#layoutyaml).

Each entry of `background:` is one layer, chosen by which of `fill`, `image`, `shader` or `video` it has. Layers cover the whole canvas and are drawn in order, the first at the bottom.

### Every layer

Every layer takes these.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `opacity` | number (fraction) | 0 to 1 | `1` | How solid the layer is: 0 hides it, 1 is fully solid. |
| `blend` | choice | `normal`, `multiply`, `screen`, `overlay`, `darken`, `lighten`, `add` | `normal` | How the layer mixes with the layers under it. |
| `hidden` | true or false |  | `false` | Leaves the layer off the screen; `marquee validate` still checks it. |
| `locked` | true or false |  | `false` | Stops the theme editor changing the layer. The screen ignores it. |
| `name` | text or number | up to 64 characters |  | What the theme editor's layers list calls the layer: up to 64 characters (VS Code may count an emoji as two), not necessarily unique. The screen ignores it. |

### Fill layer

A color or gradient.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `fill` | paint |  | required | The color or gradient. |

### Image layer

A picture from the theme folder.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `image` | file |  | required | The image file, relative to the theme folder: PNG, JPEG, WebP or another format Skia reads. |
| `fit` | choice | `cover`, `contain`, `stretch`, `none` | `cover` | How the picture fills its box: cover crops to fill it, contain fits inside it, stretch distorts to fill it, and none keeps its own size, centred. |

### Shader layer

An SkSL shader that paints the canvas each frame. Marquee sets its `time`, `day` and `resolution` uniforms.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `shader` | file |  | required | The shader file (.sksl), relative to the theme folder. |
| `uniforms` | uniforms | up to 32 |  | Binds the shader's other uniforms to numbers or expressions, one per component (up to 16). A missing or stale value is 0. |
| `scale` | number (fraction) | 0.1 to 1 | `1` | Renders the shader at this fraction of the canvas size and stretches it up, which costs far less. |

### Video layer

A clip from the theme folder, played over and over.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `video` | file | `.mp4`, `.m4v`, `.mov`, `.wmv`, `.gif`, `.webp` | required | The clip, relative to the theme folder. |
| `fit` | choice | `cover`, `contain`, `stretch`, `none` | `cover` | How the picture fills its box: cover crops to fill it, contain fits inside it, stretch distorts to fill it, and none keeps its own size, centred. |

## Widgets

Goes under `widgets:` in [layout.yaml](#layoutyaml) or `children:` in [Group](#group).

Each entry of `widgets:`, and of a group's `children:`, is one widget, chosen by its `type`.

### Every widget

Every widget takes these.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `type` | choice | `text`, `value`, `bar`, `gauge`, `graph`, `image`, `video`, `shape`, `donut`, `group` | required | What kind of widget this is. |
| `at` | point |  | required | Where the widget's top-left corner goes, relative to the canvas or its group. |
| `size` | size |  | required | The widget's width and height, each at least 1. |
| `source` | expression |  |  | The value the widget shows, and what its `when` rules compare: a sensor key such as cpu.temp, or an expression. Value, bar and gauge widgets need one; a graph needs one or `series`. |
| `style` | style |  |  | How the widget looks: a style's name from `styles:`, or a style written in place. |
| `when` | list of [When rules](#when-rules) |  |  | Styles to layer on while a condition holds, in order: every rule that matches applies, so a later one wins. |
| `smooth` | number (ms) | 0 to 10000 | `0` | Eases a value, bar or gauge to a new number over this long; 0 jumps straight there. |
| `rotate` | number (degrees) | -360 to 360 | `0` | Turns the widget clockwise about its own centre. A group's children turn with it. |
| `hidden` | true or false |  | `false` | Leaves the widget off the screen; `marquee validate` still checks it. A hidden group hides its children. |
| `locked` | true or false |  | `false` | Stops the theme editor moving or resizing the widget with the mouse. The screen ignores it. |
| `name` | text or number | up to 64 characters |  | What the theme editor's layers list calls the widget: up to 64 characters (VS Code may count an emoji as two), not necessarily unique. The screen ignores it. |

### Text

Text, which can show values. A line break (`\n` in double quotes, or a `|` block) starts a new line, and with the style's `wrap` a line too wide for the box breaks at spaces. Without a `source`, its `when` rules can only use `stale`, which matches while any value in the text is missing.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `text` | text template |  |  | The text. {cpu.temp} shows a value, {cpu.temp:0.0} formats it, {gpu.vram_used\|bytes} uses a helper and {now\|time:ddd HH:mm} shows the day and time. |
| `spans` | list of [Text spans](#text-spans) | 1 to 32 |  | Instead of `text`: runs of text, each in its own style, side by side on one baseline, such as a big number with a small unit. The widget's style places them as it places text. |

Each needs exactly one of `text` and `spans`.

#### Text spans

Goes under `spans:` in [Text](#text) or `spans:` in [Value](#value).

One run of a text or value widget's `spans`. Its style goes over the widget's, and the widget's `when` rules go over every span, so a rule that turns the text red turns every span red. A line is as tall as its largest span, and its spans share the baseline of the tallest.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `text` | text template |  | required | The span's text, a template as a text widget's is, where a value widget's {0} is its value. A line break in it starts a new line. |
| `style` | style | only `extends`, `font`, `size`, `weight`, `color`, `shadow`, `glow`, `letter_spacing`, `text_stroke`, `text_fill`, `italic`, `underline`, `strike`, `case` |  | The span's letters: a style's name, or a style written in place, with the text keys only. The box keys, such as `fill`, `padding`, `align` and `wrap`, are the widget's: written in place they are an error, and a named style's are left out without a warning. |

### Value

One value as text, laid out as a text widget's is. A missing or stale value shows as —. A value with a smaller unit, such as a big 52 with a small °C, takes `spans` instead of `format`.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `source` | expression |  | required | The value to show: a sensor key such as cpu.temp, or an expression. |
| `format` | format |  |  | How to write the value: a .NET format such as "{0:0.0}°C", duration, bytes, or time for Unix seconds as a local time ("time:HH:mm" gives a pattern). Left out, a number shows up to two decimals. |
| `spans` | list of [Text spans](#text-spans) | 1 to 32 |  | Instead of `format`: runs of text, each in its own style, side by side on one baseline, as a text widget's spans are. {0} in them is the value after `smooth`, and {0:0.0} formats it. A missing or stale value shows a single — in the first span's style. |

Each takes at most one of `format` and `spans`.

### Bar

A bar filled in proportion to a value: `track` paints the empty part, `fill` (or the style's color) the full part.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `source` | expression |  | required | The value that fills the bar: a sensor key or an expression. |
| `min` | number |  | `0` | The value at which the bar is empty. |
| `max` | number |  | `100` | The value at which the bar is full. It must be more than min. |
| `direction` | choice | `horizontal`, `vertical` | `horizontal` | Which way it fills: horizontal fills left to right, vertical bottom to top. |
| `segments` | whole number | 2 to 120 |  | Cuts the bar into this many equal segments, like a VU meter. A segment lights once the value reaches its middle; `fill` paints the lit ones and `track` the rest, and `radius` rounds their corners. |
| `gap` | number (px) | at least 0 | `4` | The space between segments, used only with `segments`. Each segment must keep at least 1 px. |

### Gauge

An arc filled in proportion to a value, in the largest square that fits its box. Angles are clockwise from 3 o'clock; the default arc opens at the bottom.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `source` | expression |  | required | The value that fills the arc: a sensor key or an expression. |
| `min` | number |  | `0` | The value at which the arc is empty. |
| `max` | number |  | `100` | The value at which the arc is full. It must be more than min. |
| `start` | number (degrees) | -360 to 360 | `135` | Where the arc starts. |
| `sweep` | number (degrees) | -360 to 360 | `270` | How far the arc runs from its start; negative runs anticlockwise. It can't be 0. |
| `thickness` | number (px) | at least 0 | `12` | How thick the arc is; 0 draws no arc, for a dial with only a needle. |
| `segments` | whole number | 2 to 120 |  | Cuts the arc into this many equal segments, like a VU meter. A segment lights once the value reaches its middle; `fill` paints the lit ones and `track` the rest, and `radius` rounds their corners. |
| `gap` | number (degrees) | at least 0 | `3` | The space between segments, used only with `segments`. Each segment must keep at least 1°, and a full ring has a gap at the join too. |
| `needle` | block: [Gauge needle](#gauge-needle) |  |  | A needle pointing at the value, which makes the gauge a dial. |
| `ticks` | block: [Gauge ticks](#gauge-ticks) |  |  | Tick marks inside the arc, and the values of the major ones. |

#### Gauge needle

Goes under `needle:` in [Gauge](#gauge).

A needle from the dial's centre to the value, drawn over the arc and the ticks. It eases with the gauge's `smooth`, takes the style's shadow, and isn't drawn while the value is missing or stale.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `width` | number (px) | at least 1 | `4` | How wide the needle is at the centre; it narrows to a point. |
| `length` | number (fraction) | 0.1 to 1 | `0.8` | How far the needle reaches, as a fraction of the dial's radius. |
| `color` | color |  |  | The needle's and its hub's color. Left out, it is the style's color. |
| `cap` | number (px) | at least 0 | `0` | The radius of a hub circle over the needle's centre; 0 draws none. |

#### Gauge ticks

Goes under `ticks:` in [Gauge](#gauge).

Tick marks round the inside of the arc, from min to max, with the major ticks' values inside them if `labels` is set. Labels use the style's font, size, weight, color and letter spacing; a label that would overlap the one before it is left out.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `major` | whole number | 1 to 20 | `10` | How many divisions the scale has: 10 puts a major tick at every tenth of the range, 11 ticks in all, or 10 on a full circle. |
| `minor` | whole number | 0 to 10 | `0` | How many minor ticks, half as long, go between each pair of major ticks. |
| `length` | number (px) | at least 1 | `10` | How long the major ticks are, inwards from the arc. |
| `width` | number (px) | at least 1 | `2` | How thick every tick is. |
| `color` | color |  |  | The ticks' color. Left out, it is the style's color. |
| `labels` | format or `false` |  |  | How to write each major tick's value, as a value widget's `format` does, such as "{0:0}". Left out, or false, the ticks have no labels. |

### Graph

A chart of sensors' recent values, newest at the right: one `source`, or up to 8 in `series`, on one scale. The stroke draws a line, `fill` the area under it or the columns, and `track` the background.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `source` | sensor key |  |  | The sensor whose history is drawn: one key, such as gpu.load, not an expression. It's what the graph's `when` rules compare. |
| `series` | list of [Graph series](#graph-series) | 1 to 8 |  | Several sensors on one chart, each with its own history and style, drawn in order so the last is in front. A graph with series takes no `when` rules: style each series instead. |
| `kind` | choice | `line`, `area`, `columns` | `line` | How each series is drawn: a line (with the area under it if the style sets `fill`), an area filled under a line, or one column per value. |
| `min` | number |  |  | The value at the bottom. Left out, the scale fits the values shown; a columns graph's also takes in 0, so its columns rise from 0. |
| `max` | number |  |  | The value at the top; more than min if both are set. Left out, the scale fits the values shown (and 0, on a columns graph). |
| `points` | whole number | 2 to 600 | `60` | How many recent values span the width, one per sensor update. |
| `grid` | block: [Graph grid](#graph-grid) |  |  | Horizontal lines at even steps of the scale, behind the data. |

Each needs exactly one of `source` and `series`.

#### Graph grid

Goes under `grid:` in [Graph](#graph).

Horizontal lines across a graph at even steps of its scale, over the track and under the data.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `lines` | whole number | 1 to 20 | `4` | How many lines; they split the height into one more band than this. |
| `color` | color |  | `#FFFFFF26` | The lines' color. |
| `width` | number (px) | at least 0 | `1` | How thick each line is; 0 draws none. |

#### Graph series

Goes under `series:` in [Graph](#graph).

One sensor on a graph with several. Its style is layered over the graph's, so the graph can set the line width once and each series its color; box keys such as `track`, `radius` and `opacity` stay the graph's.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `source` | sensor key |  | required | The sensor whose history this series draws: one key, not an expression. |
| `style` | style |  |  | How this series looks over the graph's own style: a style's name, or one written in place. |

### Image

A picture from the theme folder, over the style's `fill` and under its stroke, clipped to the box. The style's `shadow` is the box's, so a picture that leaves part of the box empty still casts the whole box's shadow.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `src` | file |  | required | The image file, relative to the theme folder: PNG, JPEG, WebP or another format Skia reads. |
| `fit` | choice | `cover`, `contain`, `stretch`, `none` | `contain` | How the picture fills its box: cover crops to fill it, contain fits inside it, stretch distorts to fill it, and none keeps its own size, centred. |

### Video

A clip from the theme folder, played over and over, over the style's `fill` and under its stroke, clipped to the box. The box can cover at most as many pixels as the canvas (1920×480), and a theme can have at most 4 videos, counting its video layers and video widgets.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `src` | file | `.mp4`, `.m4v`, `.mov`, `.wmv`, `.gif`, `.webp` | required | The clip, relative to the theme folder. |
| `fit` | choice | `cover`, `contain`, `stretch`, `none` | `cover` | How the picture fills its box: cover crops to fill it, contain fits inside it, stretch distorts to fill it, and none keeps its own size, centred. |

### Shape

A rectangle, ellipse, line or arc, drawn with its style's `fill`, `stroke` and `opacity`, and a `shadow` and `glow` that follow its outline as they follow text. A `source` and `when` rules can restyle it, so a shape can change color on a condition.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `size` | size | at least 1, or 0 with `shape: line` | required | The shape's width and height, each at least 1; a line's may be 0 one way, which makes it horizontal or vertical. |
| `shape` | choice | `rect`, `ellipse`, `line`, `arc` | required | Which shape: a rect has the style's `radius` corners, an ellipse fills the box, a line runs from the top-left corner to the bottom-right and is drawn by its stroke alone (which it must have), and an arc is a band like a gauge's. |
| `start` | number (degrees) | -360 to 360 | `135` | Where the arc starts, clockwise from 3 o'clock. Only with `shape: arc`. |
| `sweep` | number (degrees) | -360 to 360 | `270` | How far the arc runs from its start; negative runs anticlockwise. It can't be 0. Only with `shape: arc`. |
| `thickness` | number (px) | at least 1 | `12` | How thick the arc's band is. The style's `stroke.cap` shapes its ends. Only with `shape: arc`. |

### Donut

A pie or donut that splits a whole between its slices, in the largest square that fits its box. Each slice's angle is its share of the live slices' total, so a stale slice takes no room and shows only as a notch: its gap, at least 2° wide. Angles are clockwise from 3 o'clock, as a gauge's are. `track` paints the whole ring behind the slices; `fill`, `stroke`, `glow` and `shadow` style every slice, under its own style; and the text style sets the labels.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `hole` | number (fraction) | 0 to 0.9 | `0.6` | The hole in the middle, as a fraction of the radius; 0 is a pie. |
| `start` | number (degrees) | -360 to 360 | `-90` | Where the first slice starts: -90 is 12 o'clock, 0 is 3 o'clock. |
| `gap` | number (degrees) | 0 to 90 | `2` | The space between neighbouring slices. All the gaps together take at most half the circle. |
| `labels` | choice | `outside`, `inside`, `none` | `none` | Where each slice's label goes: outside the ring, inside it on its middle, or nowhere. A label is left out when its slice is too thin to hold it, when it would overlap the label before it, or, inside, when it would run past the ring. Outside labels stay inside the box across, so leave room beside the ring for them. |
| `format` | format |  |  | How to write each slice's share of the whole, in percent, after its label, such as "{0:0}%". Left out, labels show only the slice's label. |
| `slices` | list of [Donut slices](#donut-slices) | 2 to 12 | required | The parts of the whole, drawn clockwise from start. |

#### Donut slices

Goes under `slices:` in [Donut](#donut).

One part of a donut's whole. Its style goes over the donut's, and the donut's `when` rules go over every slice.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `source` | expression |  | required | The slice's value: a sensor key or an expression. A value of 0 or less takes no room. |
| `label` | text or number |  |  | The text the slice's label shows. |
| `style` | style |  |  | The slice's `fill` (default: the style's color), `stroke`, `glow` and `shadow`: a style's name, or a style written in place. |

### Group

A box that holds other widgets. Its children are placed from its top-left corner, clipped to it, and take on its text style and letter effects. Groups nest up to 16 deep.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `children` | list of [Widgets](#widgets) |  | required | The widgets inside, drawn in order. |

## When rules

Goes under `when:` in [Widgets](#widgets).

Each rule has one condition and a style to layer on while it holds. Every matching rule applies, in order. Above, below and equals compare the widget's `source`; a text widget's stale also matches while any value in its text is missing.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `above` | number |  |  | Matches while the widget's source is above this number. |
| `below` | number |  |  | Matches while the widget's source is below this number. |
| `equals` | text or number |  |  | Matches while the widget's source is this number or this text. |
| `stale` | true or false |  |  | Matches while the widget's source has no fresh value. It only takes true. |
| `style` | style |  | required | The style to layer on: a style's name, or a style written in place. |

Each needs exactly one of `above`, `below`, `equals` and `stale`.

## LED ring

Goes under `ring:` in [layout.yaml](#layoutyaml).

What the screen's ring of 60 LEDs shows. A key left out takes the default below, so `ring: { color: "#3A7BFF" }` shows that blue, static, at full brightness. `ring:` in config.yaml overrides it.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `color` | color |  | `#FFFFFF` | The LEDs' color. An alpha byte dims it. |
| `effect` | choice | `static`, `breathe`, `spin`, `rainbow` | `static` | static is the color on every LED, breathe fades it in and out, spin sends a bright segment round the ring, and rainbow turns the hues round it, ignoring color. |
| `speed` | number (times) | 0.1 to 10 | `1` | How fast the effect runs: 2 is twice as fast. |
| `brightness` | number (%) | 0 to 100 | `100` | How bright the LEDs are. |
| `source` | expression |  |  | What the ring's `when` rules compare: a sensor key or an expression. |
| `when` | list of [Ring when rules](#ring-when-rules) |  |  | Changes to make while a condition holds. Every matching rule applies, in order. |

### Ring when rules

Goes under `when:` in [LED ring](#led-ring).

Each rule has one condition, which compares the ring's `source`, and changes at least one of color, effect, speed and brightness while it holds. A later rule wins for each key it sets.

| Key | Kind | Values | Default | What it does |
|---|---|---|---|---|
| `above` | number |  |  | Matches while the ring's source is above this number. |
| `below` | number |  |  | Matches while the ring's source is below this number. |
| `equals` | text or number |  |  | Matches while the ring's source is this number or this text. |
| `stale` | true or false |  |  | Matches while the ring's source has no fresh value. It only takes true. |
| `color` | color |  |  | The LEDs' color. An alpha byte dims it. |
| `effect` | choice | `static`, `breathe`, `spin`, `rainbow` |  | static is the color on every LED, breathe fades it in and out, spin sends a bright segment round the ring, and rainbow turns the hues round it, ignoring color. |
| `speed` | number (times) | 0.1 to 10 |  | How fast the effect runs: 2 is twice as fast. |
| `brightness` | number (%) | 0 to 100 |  | How bright the LEDs are. |

Each needs exactly one of `above`, `below`, `equals` and `stale`, and at least one of `color`, `effect`, `speed` and `brightness`.

## Kinds of value

| Kind | How to write it |
|---|---|
| number | A number, such as 12, -4 or 0.5. |
| whole number | A whole number, such as 30. |
| true or false | true or false. |
| text or number | A word or a number, such as idle or 3. |
| choice | One of the words listed for the key. |
| color | A quoted hex color: "#RGB", "#RRGGBB" or "#RRGGBBAA", where AA is the alpha, or "transparent". Quote it: an unquoted # starts a YAML comment. |
| paint | A color, or a gradient: { linear: ["#000000", "#1E2A44"], angle: 90 } or { radial: ["#1E2A44", "#000000"] }. Either takes stops to place its colors, as in { linear: ["#000000", "#36D1DC", "#FFFFFF"], stops: [0, 0.8, 1] }, and a radial one center and radius to move and size it: { radial: ["#FFFFFF60", "#00000000"], center: [0.3, 0.4], radius: 0.8 }. |
| font | A font installed on this PC, by name, or a .ttf, .otf or .ttc file in the theme folder. A font that isn't there falls back to Inter, with a warning. |
| font weight | 100 to 900, or light (300), normal (400), medium (500), semibold (600) or bold (700). |
| expression | A sensor key such as cpu.temp, or arithmetic on keys and numbers such as "max(cpu.load, gpu.load)" or "claude.five_hour.resets_at - now". |
| sensor key | One sensor key, such as gpu.load. |
| text template | Text with values in braces: "{cpu.temp:0}°C". {key:0.0} formats a number, {key\|duration} and {key\|bytes} use a helper, {now\|time:HH:mm} writes Unix seconds as a local time (the PC's short time without a pattern), and {{ and }} are braces. Letters in a time pattern are pattern letters: quote words, as in {now\|time:'Today is' dddd}, and write a letter alone as %H. The pattern runs to the closing brace, so spaces before it are kept. {now\|date:D} uses the same date and time patterns, defaulting to the PC's short date without a pattern; D is a long date, d a short date, M the month and day, and dddd the weekday. |
| format | A .NET format that includes {0}, such as "{0:0.0}°C", or a helper: duration, bytes, or time with an optional date and time pattern, such as "time:HH:mm". Letters in the pattern are pattern letters: quote words, as in "time:'at' HH:mm", and write a letter alone as "time:%H". Spaces in it are kept. |
| file | A path relative to the theme folder, such as images/logo.png. It can't leave the folder. |
| point | [x, y] in px, from the top-left corner. |
| size | [width, height] in px. |
| style name | The name of a style in styles:. |
| style | A style's name from styles:, or a style written in place, such as { color: "#FF5A36" }. |
| block | A mapping with the keys listed for it. |
| list | A list, one entry per - line or [a, b] on one line. |
| named entries | A mapping from names you choose to entries. |
| uniforms | A mapping from uniform names to a number or expression, or a list of them for a float2, float3 or float4. |
