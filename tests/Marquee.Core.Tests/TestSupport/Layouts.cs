using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Tests.TestSupport;

static class Layouts
{
    public static ThemeSpec Parse(string yaml) => LayoutParser.Parse(yaml);

    /// Parses a layout that must fail, and returns the error.
    public static ThemeException Error(string yaml) => Assert.Throws<ThemeException>(() => LayoutParser.Parse(yaml));
}
