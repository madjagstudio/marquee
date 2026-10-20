using Marquee.Core.Config;

namespace Marquee.Core.Tests;

/// The lconnect.handover edits read back through ConfigParser: the edited file parses to the same config as a file
/// written by hand with that value. ConfigEditorTests already checks these edits with YamlDotNet; this adds
/// ConfigParser's view.
public class ConfigEditorHandoverParserTests
{
    [Theory]
    [InlineData("lconnect: { handover: off }\n", "lconnect: { handover: auto }")]
    [InlineData("theme: neon   # mine\nlconnect:\n  handover: ask\n", "theme: neon\nlconnect: { handover: auto }")]
    [InlineData("theme: neon\r\n", "theme: neon\nlconnect: { handover: auto }")]
    [InlineData("lconnect: {}\nfps: 30\n", "fps: 30\nlconnect: { handover: auto }")]
    [InlineData("", "lconnect: { handover: auto }")]
    public void A_handover_edit_parses_to_the_same_config_as_the_hand_written_file(string before, string handWritten) =>
        Assert.Equal(ConfigParser.Parse(handWritten), ConfigParser.Parse(ConfigEditor.Set(before, "lconnect.handover", "auto")));
}
