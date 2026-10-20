using Marquee.Core.Config;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;

namespace Marquee.Core.Tests;

/// YamlDotNet reports a few malformed texts with an ArgumentException rather than a YamlException. A hand edit like
/// that must still be a theme or config error, which keeps the last good layout, and never an exception nothing expects.
public class YamlSyntaxTests
{
    [Theory]
    [InlineData("- :\n")]
    [InlineData("canvas: { orientation: landscape }\nwidgets:\n  - :\n")]
    public void A_layout_YamlDotNet_rejects_with_an_argument_error_is_a_theme_error(string yaml)
    {
        var ex = Assert.Throws<ThemeException>(() => LayoutParser.Parse(yaml));

        Assert.StartsWith("YAML syntax error", ex.Reason);
        Assert.DoesNotContain("(Parameter", ex.Reason);
    }

    [Fact]
    public void A_layout_YamlDotNet_rejects_with_an_argument_error_is_collected_by_validate()
    {
        var errors = new List<ThemeException>();

        LayoutParser.Parse("- :\n", errors);

        Assert.StartsWith("YAML syntax error", Assert.Single(errors).Reason);
    }

    [Fact]
    public void A_config_YamlDotNet_rejects_with_an_argument_error_is_a_config_error()
    {
        Assert.Throws<ConfigException>(() => ConfigParser.Parse("- :\n"));
    }
}
