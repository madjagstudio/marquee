using System.Globalization;
using System.Text;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Validation;

/// `marquee validate <theme-folder|zip> [--fps N]`: every problem in a theme, one a line as `file:line: error: …`,
/// sorted by file then line, then a summary naming the orientation and size the theme targets. Exits 0 with no errors
/// (warnings allowed) and 1 with any. Needs no config.yaml, screen or providers: a theme's author runs it before
/// sharing, and the gallery before installing.
static class ValidateCommand
{
    /// Null means the arguments were wrong and the caller should print usage.
    public static int? Run(string[] args) => Run(args, ThemeValidator.Default);

    internal static int? Run(string[] args, ThemeValidator validator)
    {
        if (args is not [var path, .. var options] || path.StartsWith("--", StringComparison.Ordinal)) return null;

        int? fps = null;
        for (var i = 0; i < options.Length; i++)
        {
            var value = i + 1 < options.Length ? options[i + 1] : null;
            switch (options[i])
            {
                case "--fps" when Integer(value) is { } f && f is >= 1 and <= CanvasSpec.MaxFps: fps = f; i++; break;
                default: return null;
            }
        }

        var report = validator.Validate(path, fps);
        foreach (var issue in report.Issues)
            Console.WriteLine(Line(issue, path));
        Console.WriteLine(Summary(report));
        return report.Errors > 0 ? 1 : 0;
    }

    /// An issue about the theme as a whole is shown on the path as it was given.
    static string Line(ThemeIssue issue, string path)
    {
        var file = Printable(issue.File.Length > 0 ? issue.File : path);
        var message = Printable(issue.Message);
        return (issue.Severity, issue.Line) switch
        {
            (IssueSeverity.Error, > 0) => Strings.ValidateErrorOnLine(file, issue.Line, message),
            (IssueSeverity.Error, _) => Strings.ValidateError(file, message),
            (_, > 0) => Strings.ValidateWarningOnLine(file, issue.Line, message),
            _ => Strings.ValidateWarning(file, message),
        };
    }

    static string Summary(ThemeReport report)
    {
        var errors = report.Errors switch { 0 => null, 1 => Strings.OneError, var n => Strings.ManyErrors(n) };
        var warnings = report.Warnings switch { 0 => null, 1 => Strings.OneWarning, var n => Strings.ManyWarnings(n) };
        var counts = (errors, warnings) switch
        {
            (null, null) => Strings.NoProblemsFound,
            ({ } e, { } w) => Strings.ErrorsAndWarnings(e, w),
            _ => errors ?? warnings!,
        };

        var name = Printable(report.Name);
        if (report.Canvas is not { } canvas) return Strings.ValidateSummaryNoCanvas(name, counts);
        // The orientation is layout.yaml's own word for it, as the author wrote it, so it isn't translated.
        var orientation = canvas.Orientation.ToString().ToLowerInvariant();
        return Strings.ValidateSummary(name, orientation, canvas.Size.Width, canvas.Size.Height, counts);
    }

    /// A theme is someone else's text, and what validate prints quotes it: layout.yaml's keys and values, a zip's entry
    /// names. Printed as they are, control characters (C0, DEL and C1, which include the terminal's escape sequences)
    /// could rewrite the terminal, erasing the error lines above them, say, and the bidirectional overrides reorder
    /// what is shown. Each is printed as \uXXXX instead.
    static string Printable(string text)
    {
        if (!text.Any(Hidden)) return text;
        var printable = new StringBuilder(text.Length + 16);
        foreach (var c in text)
        {
            if (Hidden(c)) printable.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
            else printable.Append(c);
        }

        return printable.ToString();
    }

    static bool Hidden(char c) => char.IsControl(c) || c is (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');

    static int? Integer(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
