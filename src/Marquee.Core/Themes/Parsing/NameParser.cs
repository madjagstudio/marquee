using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// The `name` every widget and background layer may have. The theme editor shows it in its layers list; the renderer
/// ignores it, and two things may share one. Bidi controls such as U+202E are kept: showing a name safely is the
/// editor's job, which escapes them as `marquee validate` does (ValidateCommand.Printable), so a name can't scramble
/// the list.
static class NameParser
{
    /// Long enough to describe what a widget or layer is, short enough to fit one row of the editor's layers list.
    /// Counted in characters as people see them, so an emoji with a skin tone is one, not four UTF-16 units.
    public const int MaxLength = 64;

    public static string Parse(YamlNode node)
    {
        var name = Yaml.Text(node, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw Yaml.Error(node, "name is only spaces");
        return new StringInfo(name).LengthInTextElements <= MaxLength
            ? name
            : throw Yaml.Error(node, $"name is too long (the limit is {MaxLength} characters)");
    }
}
