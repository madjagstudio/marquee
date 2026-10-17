using System.Globalization;
using Marquee.Core.Devices;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Preview;

/// `marquee run <theme-folder>`: live sensors → theme → screen (or --sink), with hot reload and an optional preview.
static class RunCommand
{
    static readonly TimeSpan StatusEvery = TimeSpan.FromSeconds(1);

    /// Null means the options were wrong and the caller should print usage.
    public static int? Run(string folder, string[] options)
    {
        var orientation = Orientation.Landscape;
        int? fps = null;
        var sensors = SensorSources.Default;
        string? sink = null;
        var preview = false;
        int? frames = null;

        for (var i = 0; i < options.Length; i++)
        {
            var value = i + 1 < options.Length ? options[i + 1] : null;
            switch (options[i])
            {
                case "--orientation" when Screens.ParseOrientation(value) is { } o: orientation = o; i++; break;
                case "--fps" when int.TryParse(value, out var f) && f is >= 1 and <= CanvasSpec.MaxFps: fps = f; i++; break;
                case "--sensors" when SensorSources.IsValid(value): sensors = value!; i++; break;
                case "--sink" when value is not null: sink = value; i++; break;
                case "--frames" when int.TryParse(value, out var n) && n > 0: frames = n; i++; break;
                case "--preview": preview = true; break;
                default: return null;
            }
        }

        var log = new ConsoleLog();
        var store = new SensorStore();
        using var providers = new ProviderRunner(store, log);
        if (SensorSources.Create(sensors, log) is { } provider)
            providers.Start(provider);

        var size = orientation.CanvasSize();
        using var device = sink is not null ? new FileSinkDevice(sink, size.Width, size.Height) : Screens.Find(orientation);
        if (device is null)
        {
            Console.Error.WriteLine("No supported screen found. Run `marquee devices` to check, or use --sink <folder>.");
            return 1;
        }

        using var loop = new RenderLoop(device, store, log, fps);
        PreviewHost? previewHost = null;
        var showing = false;
        using var watcher = new ThemeWatcher(folder,
            theme =>
            {
                foreach (var warning in theme.Warnings) log.Warn(warning);
                log.Info($"Showing theme '{theme.Name}'.");
                previewHost?.ShowError(null);
                loop.SetTheme(theme);
                showing = true;
            },
            error =>
            {
                log.Error(showing ? $"{error.Message} (the last good layout stays on screen until the file is fixed)" : error.Message);
                previewHost?.ShowError(error.Message);
            });

        // With no last good layout yet, a broken theme at startup has nothing to fall back to.
        if (!watcher.Reload(retryIfLocked: false)) return 1;

        if (frames is { } count)
        {
            for (var i = 0; i < count; i++)
                loop.Step(TimeSpan.FromSeconds(i / (double)loop.Fps));
            if (loop.FramesPushed < count)
            {
                log.Error($"only {loop.FramesPushed} of {count} frame(s) reached {device.Info.Name}.");
                return 1;
            }

            log.Info($"Rendered {count} frame(s) to {device.Info.Name}.");
            return 0;
        }

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

        if (preview)
        {
            previewHost = PreviewHost.Open(Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))), size);
            previewHost.Closed += stop.Set;
            loop.FrameRendered += previewHost.ShowFrame;
        }

        watcher.Start();
        loop.Start();
        log.Info($"Rendering to {device.Info.Name}. Edit the theme and save to reload; press Ctrl+C to stop.");

        for (var seconds = 1; !stop.Wait(StatusEvery); seconds++)
        {
            var stats = loop.Stats;
            var status = string.Create(CultureInfo.InvariantCulture,
                $"{stats.Fps:0.0} fps (target {loop.Fps}) · render {stats.RenderMs:0.0} ms · send {stats.PushMs:0.0} ms · {loop.FramesPushed} frames sent");
            previewHost?.ShowStatus(status);
            if (seconds == 5 || seconds % 60 == 0) log.Info(status);
        }

        Interlocked.Exchange(ref stopping, 1); // stopping now (the preview may have closed), so Ctrl+C won't touch `stop`
        loop.Stop();
        previewHost?.Dispose();
        return 0;
    }
}
