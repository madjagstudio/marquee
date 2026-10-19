using Marquee.Core.Config;
using Marquee.Core.Logging;
using Marquee.Host.Resources;

namespace Marquee.Host;

/// Reading config.yaml (MarqueePaths.ConfigFile, or the file `--config` names).
public static class ConfigFile
{
    /// For startup: never fails. A missing file is the defaults, silently, since most people never write one. A
    /// broken or unreadable file is the defaults too, with the error (and its line) logged, so Marquee still starts.
    public static MarqueeConfig Load(string path, ILog log)
    {
        try
        {
            return Read(path);
        }
        catch (ConfigException ex)
        {
            log.Error(Strings.ConfigInvalidUsingDefaults(ex.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error(Strings.ConfigUnreadable(path, ex.Message));
        }

        return MarqueeConfig.Default;
    }

    /// For a reload: a missing file is the defaults, a broken one throws ConfigException, and one that can't be read
    /// (an editor holding it mid-save) throws the IOException or UnauthorizedAccessException, so the caller can keep
    /// the last good config or try again.
    public static MarqueeConfig Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return MarqueeConfig.Default;
        }

        return ConfigParser.Parse(text);
    }
}
