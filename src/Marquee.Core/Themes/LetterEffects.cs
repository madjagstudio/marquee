namespace Marquee.Core.Themes;

/// An outline round each letter: `Width` px of `Paint` showing outside the letters, drawn behind their fill. There is
/// none without a paint, as for a box's stroke.
public sealed record TextStrokeSpec(float? Width = null, PaintSpec? Paint = null)
{
    public static TextStrokeSpec? Merge(TextStrokeSpec? under, TextStrokeSpec? over) =>
        under is null ? over : over is null ? under : new(over.Width ?? under.Width, over.Paint ?? under.Paint);
}

/// How a style changes the case of its text, after the template is filled in.
public enum LetterCase { None, Upper, Lower, Title }
