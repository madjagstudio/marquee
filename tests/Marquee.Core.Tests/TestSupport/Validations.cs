using Marquee.Core.Themes;
using Marquee.Core.Themes.Validation;

namespace Marquee.Core.Tests.TestSupport;

static class Validations
{
    /// A validator with the free cost probes unless a test gives others, so results don't depend on the machine.
    public static ThemeValidator Validator(ThemeLimits? limits = null, ShaderProbe? shaders = null, VideoProbe? videos = null) =>
        new(shaders ?? TestProbes.Free, videos ?? TestProbes.Free, limits ?? ThemeLimits.Default);

    public static ThemeReport Validate(string path, ThemeLimits? limits = null, ShaderProbe? shaders = null,
        VideoProbe? videos = null, int? fps = null) =>
        Validator(limits, shaders, videos).Validate(path, fps);

    /// Each issue as "file:line: severity: message", which keeps assertions on several issues readable.
    public static List<string> Lines(ThemeReport report) =>
        report.Issues.Select(i => $"{i.File}:{i.Line}: {(i.Severity == IssueSeverity.Error ? "error" : "warning")}: {i.Message}").ToList();
}
