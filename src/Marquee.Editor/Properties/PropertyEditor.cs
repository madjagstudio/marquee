using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Properties;

/// The control a properties row shows, one per kind of key.
public enum PropertyEditor
{
    /// A slider and a number box, with the unit.
    Number,

    /// A checkbox.
    Boolean,

    /// A dropdown of the key's values.
    Choice,

    /// Named weights and 100 to 900, or a number typed.
    Weight,

    /// The colour picker, with alpha and hex.
    Color,

    /// The colour picker, or a gradient through the gradient editor.
    Paint,

    /// System fonts, Inter and the theme's font files, with "Import…".
    Font,

    /// A key picker with a free-text expression box, checked as it is typed.
    Key,

    /// A sensor, fixed number or advanced expression for a source.
    ValuePicker,

    /// A text box with a live preview of the result.
    Template,

    /// A plain text box.
    Text,

    /// The theme's files of the right type, with "Import…".
    File,

    /// Two number boxes: x and y, or width and height.
    Pair,

    /// A collapsible sub-section of rows.
    Block,

    /// Items to add, remove and reorder, each a sub-section.
    List,

    /// What the file says, which the panel doesn't edit (a map of names, a shader's uniforms).
    ReadOnly,
}

public static class PropertyEditors
{
    /// The control for a catalog kind.
    public static PropertyEditor For(SchemaKind kind) => kind switch
    {
        SchemaKind.Number or SchemaKind.Integer => PropertyEditor.Number,
        SchemaKind.Boolean => PropertyEditor.Boolean,
        SchemaKind.Enum or SchemaKind.StyleName => PropertyEditor.Choice,
        SchemaKind.FontWeight => PropertyEditor.Weight,
        SchemaKind.Color => PropertyEditor.Color,
        SchemaKind.Paint => PropertyEditor.Paint,
        SchemaKind.FontFamily => PropertyEditor.Font,
        SchemaKind.Expression or SchemaKind.SensorKey => PropertyEditor.Key,
        SchemaKind.Template or SchemaKind.Format => PropertyEditor.Template,
        SchemaKind.String => PropertyEditor.Text,
        SchemaKind.ThemeFile => PropertyEditor.File,
        SchemaKind.Point or SchemaKind.Size => PropertyEditor.Pair,
        SchemaKind.Block or SchemaKind.Style => PropertyEditor.Block,
        SchemaKind.List => PropertyEditor.List,
        SchemaKind.Uniforms => PropertyEditor.ReadOnly,
        _ => PropertyEditor.ReadOnly,
    };
}
