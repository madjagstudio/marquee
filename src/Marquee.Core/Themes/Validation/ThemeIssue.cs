namespace Marquee.Core.Themes.Validation;

public enum IssueSeverity { Error, Warning }

/// One problem `marquee validate` found. File is relative to the theme folder, with forward slashes (layout.yaml,
/// shaders/aurora.sksl), or empty when the problem is with the theme as a whole: its size, or the zip it came in. Line
/// is 1-based within the file, or 0 when the problem isn't on one line.
public sealed record ThemeIssue(IssueSeverity Severity, string File, int Line, string Message)
{
    internal static ThemeIssue Error(string file, string message) => new(IssueSeverity.Error, file, 0, message);
}

/// What `marquee validate` found in a theme. Canvas is null when layout.yaml doesn't declare a valid one (that is among
/// the issues). Issues are sorted by file, the theme as a whole first, then by line; within a file, issues not on a
/// line come after the rest, and otherwise keep the order they were found in.
public sealed record ThemeReport(string Name, CanvasSpec? Canvas, IReadOnlyList<ThemeIssue> Issues)
{
    public int Errors => Issues.Count(i => i.Severity == IssueSeverity.Error);

    public int Warnings => Issues.Count(i => i.Severity == IssueSeverity.Warning);
}
