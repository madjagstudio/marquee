using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Tests.TestSupport;

static class Layouts
{
    public static ThemeSpec Parse(string yaml) => LayoutParser.Parse(yaml);

    /// Parses a layout that must fail, and returns the error.
    public static ThemeException Error(string yaml) => Assert.Throws<ThemeException>(() => LayoutParser.Parse(yaml));

    /// Parses the way `marquee validate` does: every error, and the spec built from what was valid.
    public static (ThemeSpec Spec, List<ThemeException> Errors) Collect(string yaml)
    {
        var errors = new List<ThemeException>();
        var spec = LayoutParser.Parse(yaml, errors);
        return (spec, errors);
    }

    /// Each error as "line: reason", which keeps assertions on several errors readable.
    public static List<string> Lines(IEnumerable<ThemeException> errors) => errors.Select(e => $"{e.Line}: {e.Reason}").ToList();
}
