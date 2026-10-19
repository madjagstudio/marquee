using Marquee.Cli.Tests.TestSupport;

namespace Marquee.Cli.Tests;

[Collection(nameof(ConsoleCapture))]
public class PushCommandTests
{
    [Fact]
    public void Push_says_http_is_off_in_config_yaml_instead_of_trying()
    {
        var config = Path.Combine(Path.GetTempPath(), "marquee-config-" + Guid.NewGuid().ToString("N")[..8] + ".yaml");
        File.WriteAllText(config, "http: off\n");
        try
        {
            using var console = new ConsoleCapture();

            Assert.Equal(1, PushCommand.Run("build", "ok", ["--config", config]));

            Assert.Equal(
                $"Custom values are turned off in {config} (http: off), so Marquee isn't listening for them. Turn http on there, or pass --port if Marquee was started with --http-port.",
                console.Error.Trim());
        }
        finally
        {
            File.Delete(config);
        }
    }
}
