using Marquee.Core;
using Marquee.Core.Config;
using Marquee.Core.Logging;
using Marquee.Host;

/// `--config <file>`, which run, sensors and push accept: read that file instead of %LOCALAPPDATA%\Marquee\config.yaml,
/// to try settings out or keep several sets side by side.
static class ConfigOption
{
    /// The config.yaml to read, as a full path.
    public static string Path(string? file) => file is null ? MarqueePaths.ConfigFile : System.IO.Path.GetFullPath(file);

    /// The settings in it; the defaults, with the reason logged, if it is broken.
    public static MarqueeConfig Load(string? file, ILog log) => Read(Path(file), named: file is not null, log);

    /// The settings in `path`, a full path. The usual config.yaml is often missing, which is fine; a file `named` on
    /// the command line that is missing is probably a typo, so that is said.
    public static MarqueeConfig Read(string path, bool named, ILog log)
    {
        if (named && !File.Exists(path)) log.Warn(Strings.ConfigFileMissing(path));
        return ConfigFile.Load(path, log);
    }
}
