using System.Globalization;
using Marquee.Core;
using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Host;
using Marquee.Preview;

/// `marquee run [<theme-folder>]`: live sensors → theme → screen (or --sink), with hot reload and an optional preview.
/// MarqueeEngine does the work; this is its console host. Settings come from the flags, then config.yaml, then the
/// defaults, and an edit to config.yaml while running is applied at once (the flags still win).
static class RunCommand
{
    static readonly TimeSpan StatusEvery = TimeSpan.FromSeconds(1);

    /// Null means the options were wrong and the caller should print usage.
    public static int? Run(string[] args)
    {
        if (Parse(args) is not { } options) return null;

        // Elevated, the log and the Claude limits go in an admin-only folder under %ProgramData%; one that can't be made
        // safe stops Marquee here, with the reason, before anything writes there.
        string logs;
        try
        {
            logs = MarqueePaths.ProcessLogs;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new ConsoleLog().Error(Strings.DataFolderUnusable(ex.Message));
            return 1;
        }

        using var fileLog = new FileLog(logs);
        var log = new TeeLog(new ConsoleLog(), fileLog);
        var configPath = ConfigOption.Path(options.ConfigFile);
        var locator = ThemeLocator.Default;
        var resolved = SettingsResolver.Resolve(options, ConfigOption.Load(options.ConfigFile, log), locator, configPath);
        if (!resolved.Succeeded)
        {
            log.Error(resolved.Error);
            return 1;
        }

        using var engine = new MarqueeEngine(resolved.Settings, log, MachineServices.Instance);
        PreviewHost? previewHost = null;
        string? banner = null; // what the preview's banner shows: the theme's warnings (a shader error, say), or a layout error
        engine.ThemeLoaded += theme =>
        {
            banner = theme.Warnings.Count > 0 ? string.Join(Environment.NewLine, theme.Warnings) : null;
            previewHost?.ShowError(banner);
        };
        engine.ThemeFailed += error =>
        {
            banner = error;
            previewHost?.ShowError(banner);
        };

        if (options.Frames is { } count) return RenderFrames(engine, count, log);
        if (!engine.Start()) return 1;

        // The default folder is %LOCALAPPDATA%\Marquee, which an unelevated log has made by now anyway. Making sure of it
        // here means a config.yaml written there later, while this runs, is seen. A --config file's folder is left alone.
        if (options.ConfigFile is null) Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        using var configWatcher = new ConfigWatcher(configPath,
            config =>
            {
                var changed = SettingsResolver.Resolve(options, config, locator, configPath);
                if (changed.Succeeded) engine.Apply(changed.Settings);
                else log.Error(Strings.SettingsUnchanged(changed.Error));
            },
            error => log.Error(Strings.SettingsUnchanged(error.Message)));
        configWatcher.Start();

        using var stop = new ManualResetEventSlim();
        var stopping = 0;
        Console.CancelKeyPress += (_, e) =>
        {
            // The first Ctrl+C stops gracefully. Any later one (shutdown is stuck, or already under way and `stop` may
            // be disposed) gets the default behavior and ends the process.
            var first = Interlocked.Exchange(ref stopping, 1) == 0;
            e.Cancel = first;
            if (first) stop.Set();
        };

        if (options.Preview)
        {
            var settings = engine.Settings;
            previewHost = PreviewHost.Open(Path.GetFileName(settings.ThemeFolder), settings.Orientation.CanvasSize());
            previewHost.Closed += stop.Set;
            engine.FrameRendered += previewHost.ShowFrame;
            previewHost.ShowError(banner); // the startup load happened before the window opened
        }

        log.Info(Strings.RenderingTo(engine.Status.Device ?? ""));

        for (var seconds = 1; !stop.Wait(StatusEvery); seconds++)
        {
            var status = engine.Status;
            var line = Strings.RenderStatus(status.Stats.Fps, status.TargetFps, status.Stats.RenderMs, status.Stats.PushMs, status.FramesSent);
            previewHost?.ShowStatus(line);
            if (seconds == 5 || seconds % 60 == 0) log.Info(line);
        }

        Interlocked.Exchange(ref stopping, 1); // stopping now (the preview may have closed), so Ctrl+C won't touch `stop`
        configWatcher.Dispose(); // no settings change may start the engine again while it stops
        engine.Stop();
        previewHost?.Dispose();
        return 0;
    }

    /// The command line after `run`: an optional theme folder, then flags. Null if a flag or its value is wrong.
    internal static RunOptions? Parse(string[] args)
    {
        var options = new RunOptions();
        var first = 0;
        if (args is [var theme, ..] && !theme.StartsWith("--", StringComparison.Ordinal))
        {
            options = options with { Theme = theme };
            first = 1;
        }

        for (var i = first; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--orientation" when Screens.ParseOrientation(value) is { } o: options = options with { Orientation = o }; i++; break;
                case "--fps" when Integer(value) is { } f && f is >= 1 and <= CanvasSpec.MaxFps: options = options with { Fps = f }; i++; break;
                case "--sensors" when SensorSourceNames.IsValid(value): options = options with { Sensors = value }; i++; break;
                case "--sink" when value is not null: options = options with { Sink = value }; i++; break;
                case "--frames" when Integer(value) is { } n && n > 0: options = options with { Frames = n }; i++; break;
                case "--preview": options = options with { Preview = true }; break;
                case "--http-port" when HttpPort.TryParse(value, out var p): options = options with { HttpEnabled = true, HttpPort = p }; i++; break;
                case "--no-http": options = options with { HttpEnabled = false }; break;
                case "--no-claude-usage": options = options with { ClaudeUsage = false }; break;
                case "--no-fps": options = options with { GameFps = false }; break;
                case "--config" when value is not null: options = options with { ConfigFile = value }; i++; break;
                default: return null;
            }
        }

        return options;
    }

    static int? Integer(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// `--frames N`: N frames at fixed times, then exit; 1 if any didn't reach the screen.
    static int RenderFrames(MarqueeEngine engine, int count, ILog log)
    {
        if (!engine.Start(liveRendering: false)) return 1;
        var screen = engine.Status.Device ?? "";
        var sent = engine.RenderFrames(count);
        if (sent < count)
        {
            log.Error(Strings.FramesNotDelivered(sent, count, screen));
            return 1;
        }

        log.Info(Strings.RenderedFrames(count, screen));
        return 0;
    }
}
