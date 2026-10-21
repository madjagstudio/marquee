namespace Marquee.Editor.Model;

/// Why the layout text doesn't load as a theme, as ThemeException says it: the reason, and its 1-based line in
/// layout.yaml, or 0 when it isn't on one line.
public sealed record LoadError(string Reason, int Line)
{
    public override string ToString() => Line > 0 ? $"layout.yaml line {Line}: {Reason}" : Reason;
}
