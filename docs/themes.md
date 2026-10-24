# Make a theme

A Marquee theme is a folder containing `layout.yaml` and any pictures, videos,
fonts or shaders it uses. You can make it in the visual editor, write the YAML
yourself, or switch between the two. Start with the editor to see your changes
as you make them.

## Start a theme

Choose **New theme…** from the editor's window bar or the tray menu. The start
screen offers **Blank**, **Gaming stats** and **System monitor**, with previews.
Choose a starting point and give your copy a name. It goes into
`%LOCALAPPDATA%\Marquee\themes`, arranged for the screen's current landscape or
portrait orientation. **Open one of yours** opens an existing theme folder
instead of making a copy.

To edit the current theme, use **Edit theme…** in the tray or run `marquee edit`.
You can also give the command a theme name or folder. Installed examples are
read-only: accept the editor's offer to make a copy, or open one read-only to
look around and use **Make a copy to edit** later.

When ready to use your theme, choose **Show on screen**. This sets the theme in
`config.yaml`; the running tray switches to it. The screen then follows each
successful save. If the status says **Tray not running**, you can still design
on the editor's canvas, but the tray must be running to drive the screen.

## Add things

**Add** opens **Add a module**, a gallery of ready-made widgets and groups.
Browse **Numbers**, **Gauges**, **Graphs**, **Time**, **Pictures**, **Shapes**,
**My modules** or **Blank widgets**. The previews show how modules look with
readings; graphs can use sample history when live history is not available.

Click a module to place it in the middle of the canvas, or drag its picture to
the spot you want. Picture and video modules ask for a file first and copy it
into the theme. A module can contain several widgets: for example, a reading
and its label. Once added, those widgets are editable like anything else.

For a first experiment, add a temperature dial, then select it and change the
reading it shows. Build a small arrangement before filling the whole screen;
it is easier to judge spacing and type size with a few items.

## Arrange the canvas

Click a widget to select it, drag to move it, and use its handles to resize or
rotate it. Shift-click adds to or removes from the selection; dragging on empty
canvas selects a box. Snap guides help line up nearby edges and centres. Hold
Alt to place freely, or Shift to keep resize proportions and snap rotation to
15-degree steps. Escape cancels a drag. Arrow keys move by one pixel, or ten
with Shift.

Use **Align** for alignment and distribution. **Fit**, **100%** and **200%**
change your view without changing the theme. Select related widgets and choose
**Group** from the context menu, or press Ctrl+G. Ctrl+Shift+G ungroups them.
Groups grow when you move or resize children beyond their bounds. If longer
text needs more space, enlarge its box so the group grows with it.
Dragging the group moves it together; double-click, or Ctrl+click, to pick a
widget inside and move that widget.

Open **Layers** from the window bar when overlapping items are hard to select.
Its tree lets you select, reorder, rename, hide and lock items, and see which
widgets belong to a group. Background layers sit behind the widgets. Keep
decorative items behind readings so they do not obscure them.

## Adjust in the properties panel

Select a widget to see **Properties**. The basic controls at the top cover
**What it shows**, colour and size. **More styling** groups further controls
under plain headings such as **Text**, **Fill and border**, **Effects**,
**Position** and **Motion**, depending on the item. **Every setting as written
in layout.yaml** exposes the YAML names when you need the full detail. Expand
the sections you need; styling sections start collapsed and remember how you
leave them during the session. Hover over controls for tooltips explaining
their purpose.

For a reading, open the value picker from **What it shows**. Sensors have plain
names such as **CPU temperature**, grouped into categories including CPU, GPU,
Memory and Gaming, with their current readings. Search the list to narrow it.
The picker also offers **Number** for a fixed value and **Advanced** for an
expression. Where a mapping is offered, **From low** and **From high** describe
the input range, and **To low** and **To high** describe the output range.

In text, **Insert value** adds a reading, **Time** or **Date** as a chip among
your words. Time and date offer formats with previews. Select a chip to change
its format, such as the number of decimal places, or remove it. This lets a
label say “CPU” beside a live temperature without writing placeholder syntax.

## Colours, fonts and styles

Click a colour swatch to choose a colour. Paints that support gradients offer
**Linear** and **Radial**: edit colours and stops, then adjust the angle or
centre and radius. Text colour, box fill and border are separate settings. For
gradient letters, use the text fill setting rather than the box's fill.

Choose a font available on the PC, use the built-in Inter, or import a font
file into the theme. A system font name relies on the receiving PC having that
font; a theme-local file travels with the theme. Check that you have permission
to share any font file you include.

Select a widget and use **Save as style…** to name its styling. Select another
widget and choose that **Named style** to reuse it. The **Styles** panel lets
you edit, rename and delete named styles. Editing one updates its users, while
settings explicitly overridden on a widget remain its own. **Clear overrides**
returns those settings to the named style. Deleting a named style copies its
appearance into its users, keeping their look.

For a one-time transfer, **Copy style** (Ctrl+Shift+C) and **Paste style**
(Ctrl+Shift+V) copy styling onto the selected widgets. Use a named style when
you want later shared edits; use a copy when the widgets should evolve separately.

## Make it react

Under **Rules**, choose **Add rule**. Conditions are **above**, **below**,
**equals** and **stale**. Above and below compare numbers; equals can compare
a number or text. Stale handles a reading that is missing or no longer fresh.
Choose a named style or **Custom** styling for the matching state.

For example, set a value widget's source to **CPU temperature**, add **above**
`80`, and choose a red colour in the rule's custom styling. The condition is
strictly greater than 80. Rules apply in order, so later matching rules can
replace earlier settings; drag to reorder them.

When the source uses one sensor, **Try a value** lets you sweep a test reading
across the threshold. The **Test values** panel also lets you pin values and
simulate **Stale** or **Missing**, or freeze the preview's clock. Watch the
canvas and matching-rule indication. **Unpin** or **Unpin all** returns to
live readings. Pins are preview values, not measurements of your hardware;
the panel identifies whether readings come from **Tray**, **Local**, **Sample**
or **Pinned** sources.

## Choose a background

Open **Layers**, then **Add layer ▾** in its **Background** section to open the
**Backgrounds** gallery. A selected layer also has a **Backgrounds** button in
Properties for replacing it. **Moving** contains live previews of the shipped
shaders: **Aurora**, **Slow waves**, **Plasma**, **Starfield**, **Gradient drift**
and **Nebula**. Choose one, then select its background layer to adjust it.

The annotated moving backgrounds have colour swatches, a steady speed slider
and a strength slider. **Reacts to** chooses a reading, with a **Calm** to
**Busy** meter showing the response. Choose **Nothing** for a calm background
without a sensor binding. Changing a reaction affects the picture's colour,
brightness or strength while its motion keeps a steady pace.

The cost bar gives a plain verdict: **Light. The screen keeps up easily.**,
**Heavy. Lower its quality if the screen stutters.**, or **Too heavy. The screen
will skip it.** Lower **Quality** to render at a smaller scale. This is an
estimate of shader work, so also judge the result on the screen.

**Still** offers **Colour**, **Gradient**, **Picture…** and **Video…**; video
plays despite being in this gallery section. Images and videos are imported
into the theme. Backgrounds can combine fill, image, video and shader layers;
their order determines what covers what. Under **More backgrounds**, **Import
a shader file…** adds your own shader.

## Keep your own modules

Select a widget or group and click **Save as my module…** in Properties, then name it.
It appears under **My modules** in the Add gallery. This is useful for a small
arrangement you want in several themes, including its styling and assets.

Right-click a saved module in the gallery for **Rename…** or **Delete module**.
Adding it to a theme copies its files into that theme, so the new instance is
editable independently. Your module collection lives in
`%LOCALAPPDATA%\Marquee\modules`; your themes live in the separate `themes`
folder.

## Set the LED ring

Click empty canvas to see **Theme settings**, then expand **LED ring**.
Set **Ring colour**, **Ring effect**,
**Effect speed** and **Ring brightness**. Effects are `static`, `breathe`,
`spin` and `rainbow`; brightness runs from 0 to 100. **What it shows** chooses
the source for the ring's rules. For example, a calm teal breathing ring can
turn red above a CPU temperature threshold.

The ring's rules use the same conditions as widget rules, but change ring
colour, effect, speed or brightness. Watch the animated preview strip while
adjusting them, and use test values to check thresholds.

The screen obeys the `ring:` setting in `config.yaml`: `ring: theme` allows the
theme's ring, `ring: off` keeps it dark, and a quoted colour such as
`ring: "#3A7BFF"` forces a steady colour. The editor shows an override notice
when configuration overrides the theme. With no theme ring block and the
configuration set to `theme`, the default is warm white at 40% brightness.

## Theme settings

Click empty canvas to see **Theme settings**. **Screen orientation** selects landscape
(1920 × 480) or portrait (480 × 1920); changing it offers to rearrange widgets.
The theme's orientation describes its layout. Set the physical screen's own
orientation separately in the tray's **Orientation** menu, including which
way up it is mounted. The window bar shows the screen orientation and size.

Under **More styling**, expand **Motion**. **Frames per second** controls
`canvas.fps`, from 1 to 60, with a default of 30. Start
at 30 and lower it if an animation costs too much. A configured `fps` in
`config.yaml` overrides the theme's rate. Shader quality and theme frame rate
are separate controls: one changes rendering detail, the other frame frequency.

## Saving and recovery

The editor autosaves changes to `layout.yaml` after a short pause; a drag becomes
one undo step and is saved after you release it. Watch **Saving…**, **Saved** or **Not saved** in
the window bar. Open the save status for details if saving fails. Ctrl+Z undoes
and Ctrl+Y redoes changes. Undo also changes the saved theme, so a screen
showing it follows that change.

If the editor closes before saving its latest changes, reopening the theme
offers crash recovery when a recovery copy is available. Restoring is undoable.
A **Crash recovery is off** notice means the recovery store could not be used;
do not assume unsaved edits will survive in that case.

You may edit `layout.yaml` by hand while the editor is open. A valid external
save reloads as one undo step. If it cannot load, editing pauses and the last
good preview remains while you fix the file. **Keep editing in your text
editor** lets you continue there. **Revert to opened version…** in the theme
menu returns to the version from when you opened the theme; Undo can bring
your subsequent changes back.

## Write YAML by hand

Create a folder such as `%LOCALAPPDATA%\Marquee\themes\my-first-theme` and put
this in its `layout.yaml`:

```yaml
canvas:
  orientation: landscape
  fps: 30
background:
  - fill: "#101820"
widgets:
  - type: text
    at: [40, 40]
    size: [600, 100]
    text: "Hello, Marquee"
    style:
      size: 48
      color: "#36D1DC"
```

Use spaces for indentation and quote hex colours so YAML does not read them
as comments. Keep referenced assets inside the theme folder. From PowerShell,
check the folder with:

```powershell
marquee validate "$env:LOCALAPPDATA\Marquee\themes\my-first-theme"
```

Validation lists errors and warnings with file locations. An error gives a
nonzero exit code; warnings still deserve attention even when validation
succeeds. Validation needs no connected screen.

For VS Code with its YAML extension, associate the file with
[`layout.schema.json`](layout.schema.json) for completion and validation.
In a repository theme under `themes/my-first-theme`, the first line can be:

```yaml
# yaml-language-server: $schema=../../docs/layout.schema.json
```

For a theme elsewhere, change that path to your local copy of the schema.
The [theme reference](themes-reference.md) lists every layout key, allowed
value and default. The [sensor key reference](keys.md) explains the readings,
their units and sources. Use those references when moving beyond the controls
you have explored in the editor.

## Write your own moving background

If you write SkSL, start with the shipped [Aurora shader](../themes/backgrounds/aurora.sksl).
Its leading comments describe the editor controls:

```glsl
// @name Aurora
// @control tint colour "Colour" default #36D1DC
// @control speed number 0.1..3 default 1 "Speed"
// @control strength number 0..1 default 0.8 "Strength"
// @reacts heat "Warmer and brighter as it gets busy"
```

Put these annotations before the shader code. `@name` names the background;
`@control` connects a uniform to a colour or numeric control, with its default
and numeric range; `@reacts` names the uniform driven by **Reacts to**. Aurora
declares `tint` as `float3` and `speed`, `strength` and `heat` as `float`.
Marquee supplies `time` and `resolution`. Aurora uses `heat` between 0 and 1
to warm, brighten and enlarge its bands.

Keep speed constant, never bound to a reading. Multiplying elapsed `time` by
a live value changes the animation's phase whenever that value changes, making
the picture jump. Drive colour, brightness or strength with readings instead.
The annotated controls keep their values constant except for the reaction
uniform. A shader without annotations still works: its uniforms are listed
for editing with the value picker. Check its cost hint after importing it and
adjust its scale if necessary.
