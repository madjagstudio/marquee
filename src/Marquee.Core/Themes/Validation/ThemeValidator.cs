using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Themes.Validation;

/// `marquee validate`: every problem in a theme folder, or a .zip of one, with its file and line. There is no second
/// set of rules here: layout.yaml goes through the layout parser in its collecting mode, and the assets through the
/// loaders `marquee run` uses, at the frame rate it would use, so a theme that validates clean loads clean. What only a
/// shared theme needs on top of that (file sizes, file count, no links, a safe zip) is checked here.
public sealed class ThemeValidator
{
    /// With the real shader and video cost checks, so their results depend on this PC, as they do for `marquee run`.
    public static ThemeValidator Default { get; } = new(ShaderCost.Estimate, VideoCost.Measure, ThemeLimits.Default);

    readonly ShaderProbe _shaderProbe;
    readonly VideoProbe _videoProbe;
    readonly ThemeLimits _limits;

    internal ThemeValidator(ShaderProbe shaderProbe, VideoProbe videoProbe, ThemeLimits limits)
    {
        _shaderProbe = shaderProbe;
        _videoProbe = videoProbe;
        _limits = limits;
    }

    /// `path` is a theme folder or a .zip of one. `fpsOverride` is `--fps`: with the theme's canvas.fps otherwise, it
    /// sets the frame budget a shader layer must fit in, as for `marquee run`. A zip is extracted into a temporary
    /// folder, deleted before this returns. Never throws for a problem with the theme; that is what the report is for.
    public ThemeReport Validate(string path, int? fpsOverride = null)
    {
        var issues = new List<ThemeIssue>();
        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            issues.Add(ThemeIssue.Error("", "This is not a valid path."));
            return Report(path, null, issues);
        }

        if (Directory.Exists(full))
            return ValidateFolder(full, Path.GetFileName(full), fpsOverride, issues);

        var isZip = string.Equals(Path.GetExtension(full), ".zip", StringComparison.OrdinalIgnoreCase);
        if (!File.Exists(full))
        {
            issues.Add(ThemeIssue.Error("", isZip ? "The zip file does not exist." : "The theme folder does not exist."));
            return Report(Path.GetFileName(full), null, issues);
        }

        if (!isZip)
        {
            issues.Add(ThemeIssue.Error("", "This is neither a theme folder nor a .zip of one."));
            return Report(Path.GetFileName(full), null, issues);
        }

        using var zip = ThemeZip.Extract(full, _limits, issues);
        var name = zip.ThemeName ?? Path.GetFileNameWithoutExtension(full);
        return zip.ThemeFolder is { } folder ? ValidateFolder(folder, name, fpsOverride, issues) : Report(name, null, issues);
    }

    ThemeReport ValidateFolder(string folder, string name, int? fpsOverride, List<ThemeIssue> issues)
    {
        var unsafeFiles = ThemeFiles.Check(folder, _limits, issues);

        var layoutPath = Path.Combine(folder, ThemeLoader.LayoutFileName);
        if (!File.Exists(layoutPath))
        {
            issues.Add(ThemeIssue.Error(ThemeLoader.LayoutFileName, $"The theme folder has no {ThemeLoader.LayoutFileName}."));
            return Report(name, null, issues);
        }

        // Already reported: a link isn't followed, and a layout.yaml over the file limit isn't read into memory.
        if (unsafeFiles.Contains(ThemeLoader.LayoutFileName))
            return Report(name, null, issues);

        string yaml;
        try
        {
            yaml = File.ReadAllText(layoutPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(ThemeIssue.Error(ThemeLoader.LayoutFileName, $"{ThemeLoader.LayoutFileName} could not be read: {ex.Message}"));
            return Report(name, null, issues);
        }

        var errors = new List<ThemeException>();
        var spec = LayoutParser.Parse(yaml, errors);
        issues.AddRange(errors.Select(e => new ThemeIssue(IssueSeverity.Error, ThemeLoader.LayoutFileName, e.Line, e.Reason)));

        // The same loading `marquee run` does, of whatever the parser could build, except that nothing through a link
        // or over the size limit is opened: those are reported above, as not followed and not read. (`marquee run`
        // does follow links, in a theme of the user's own; a shared theme with one never validates, so the gallery
        // never installs it.) Video layers start playing here; disposing stops each player's thread before it
        // returns, so nothing is left running.
        var warnings = new List<ThemeWarning>();
        ThemeAssets.Load(folder, spec, warnings, ThemeLoader.FrameRate(spec.Canvas, fpsOverride), _shaderProbe, _videoProbe,
            path => ThemeFiles.Refused(folder, ThemeFiles.Display(folder, path), unsafeFiles)).Dispose();
        issues.AddRange(warnings.Select(w => new ThemeIssue(
            w.Skipped ? IssueSeverity.Error : IssueSeverity.Warning, ThemeFiles.Display(folder, w.File), 0, w.Message)));

        return Report(name, LayoutParser.HasCanvas(spec) ? spec.Canvas : null, issues);
    }

    static ThemeReport Report(string name, CanvasSpec? canvas, List<ThemeIssue> issues) =>
        new(name, canvas, issues
            .OrderBy(i => i.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Line == 0 ? int.MaxValue : i.Line)
            .ToList());
}
