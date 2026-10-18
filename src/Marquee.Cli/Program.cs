using System.Diagnostics;
using Marquee.Core.Devices;
using Marquee.Core.Devices.UniversalScreen88;
using Marquee.Core.Usb;
using SkiaSharp;

// Messages use characters such as "—", "·" and "×"; the default console code page would mangle them. Setting it
// throws when there is no console at all (a detached process); Claude Code runs `marquee statusline` that way from
// some hosts, and it writes raw bytes anyway, so carry on without it.
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
}
catch (IOException)
{
}

return args switch
{
    ["devices"] => ListDevices(),
    ["show-image", var file, .. var options] => ShowImage(file, options),
    ["sensors", .. var options] => SensorsCommand.Run(options) ?? Usage(),
    ["run", var folder, .. var options] => RunCommand.Run(folder, options) ?? Usage(),
    ["push", var key, var value, .. var options] => PushCommand.Run(key, value, options) ?? Usage(),
    ["statusline"] => StatusLineCommand.Run(),
    ["setup-claude", .. var options] => SetupClaudeCommand.Run(options) ?? Usage(),
    _ => Usage(),
};

static int ListDevices()
{
    var supported = WinUsbTransport.Enumerate()
        .Where(e => DeviceRegistry.IsSupported(e.VendorId, e.ProductId))
        .ToList();

    if (supported.Count == 0)
    {
        Console.WriteLine("No supported screens found.");
        return 1;
    }

    foreach (var entry in supported)
        Console.WriteLine($"{entry.VendorId:X4}:{entry.ProductId:X4}  {entry.Path}");
    return 0;
}

static int ShowImage(string file, string[] options)
{
    var orientation = Orientation.Landscape;
    int? brightness = null;
    string? sink = null;
    var repeat = 1;

    for (var i = 0; i < options.Length; i++)
    {
        var value = i + 1 < options.Length ? options[i + 1] : null;
        switch (options[i])
        {
            case "--orientation" when Screens.ParseOrientation(value) is { } o: orientation = o; i++; break;
            case "--brightness" when int.TryParse(value, out var b): brightness = b; i++; break;
            case "--sink" when value is not null: sink = value; i++; break;
            case "--repeat" when int.TryParse(value, out var n) && n > 0: repeat = n; i++; break;
            default: return Usage();
        }
    }

    using var image = SKImage.FromEncodedData(file);
    if (image is null)
    {
        Console.Error.WriteLine($"Could not read image: {file}");
        return 1;
    }

    var size = orientation.CanvasSize();
    var wrongShape = orientation.IsPortrait() ? image.Width > image.Height : image.Height > image.Width;
    if (wrongShape)
        Console.Error.WriteLine(
            $"Warning: image is {image.Width}x{image.Height} but the {(orientation.IsPortrait() ? "portrait" : "landscape")} canvas is {size.Width}x{size.Height}; it will be stretched.");

    IDisplayDevice? device = sink is not null ? new FileSinkDevice(sink, size.Width, size.Height) : Screens.Find(orientation);
    if (device is null)
    {
        Console.Error.WriteLine("No supported screen found. Run `marquee devices` to check.");
        return 1;
    }

    try
    {
        device.Open();
        if (brightness is { } level) device.SetBrightness(level);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < repeat; i++) device.PushFrame(image);
        stopwatch.Stop();
        var firmware = device is UniversalScreen88Device screen && screen.FirmwareVersion is { } v ? $" (firmware {v})" : "";
        Console.WriteLine($"Sent {file} to {device.Info.Name}{firmware}.");
        if (repeat > 1)
            Console.WriteLine($"Pushed {repeat} frames, average {stopwatch.Elapsed.TotalMilliseconds / repeat:F1} ms per frame");
        return 0;
    }
    catch (DeviceBusyException ex)
    {
        Console.Error.WriteLine(ex.Message);
        foreach (var line in LConnectDetector.ConsoleHint(LConnectDetector.Default.Detect()))
            Console.Error.WriteLine(line);
        return 1;
    }
    finally
    {
        device.Dispose();
    }
}

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          marquee devices
          marquee show-image <file> [--orientation portrait|portrait-flipped|landscape|landscape-flipped] [--brightness 0-100] [--repeat N] [--sink <folder>]
          marquee sensors [--sensors lhm|hwinfo|none] [--no-claude-usage]
          marquee run <theme-folder> [--orientation ...] [--fps 1-60] [--sensors lhm|hwinfo|none] [--preview] [--sink <folder>] [--frames N]
                      [--http-port 1024-65535 | --no-http] [--no-claude-usage]
          marquee push <key> <value> [--ttl 30s|5m|2h] [--port 1024-65535]
          marquee setup-claude [--undo] [--dry-run]
          marquee statusline        (Claude Code runs this; see setup-claude)
        """);
    return 2;
}
