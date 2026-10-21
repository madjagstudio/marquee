namespace Marquee.Editor.Model;

/// Whether the editor may save a theme folder. A read-only one opens for viewing, with Reason saying why in words for
/// the user, and "Make a copy to edit" (ThemeOpening.MakeCopy).
public sealed record ThemeAccess(bool IsReadOnly, string? Reason)
{
    public static readonly ThemeAccess Editable = new(false, null);

    public static ThemeAccess ReadOnly(string reason) => new(true, reason);
}
