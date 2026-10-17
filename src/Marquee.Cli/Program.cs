using System.Diagnostics;
using Marquee.Core.Devices;
using Marquee.Core.Devices.UniversalScreen88;
using Marquee.Core.Usb;
using SkiaSharp;

// Messages use characters such as "—", "·" and "×"; the default console code page would mangle them.
Console.OutputEncoding = System.Text.Encoding.UTF8;

return args switch
{
    ["devices"] => ListDevices(),
    ["show-image", var file, .. var options] => ShowImage(file, options),
    ["sensors", .. var options] => SensorsCommand.Run(options) ?? Usage(),
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
            case "--orientation" when value is "portrait": orientation = Orientation.Portrait; i++; break;
            case "--orientation" when value is "portrait-flipped": orientation = Orientation.PortraitFlipped; i++; break;
            case "--orientation" when value is "landscape": orientation = Orientation.Landscape; i++; break;
            case "--orientation" when value is "landscape-flipped": orientation = Orientation.LandscapeFlipped; i++; break;
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

    IDisplayDevice? device = sink is not null ? new FileSinkDevice(sink, size.Width, size.Height) : FindScreen(orientation);
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
        if (LConnectDetector.IsRunning())
            Console.Error.WriteLine("L-Connect 3 is running. Quit it from its tray icon, then try again.");
        return 1;
    }
    finally
    {
        device.Dispose();
    }
}

static IDisplayDevice? FindScreen(Orientation orientation) =>
    WinUsbTransport.Enumerate()
        .Where(e => DeviceRegistry.IsSupported(e.VendorId, e.ProductId))
        .Select(e => DeviceRegistry.Create(e, WinUsbTransport.Open, orientation))
        .FirstOrDefault();

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          marquee devices
          marquee show-image <file> [--orientation portrait|portrait-flipped|landscape|landscape-flipped] [--brightness 0-100] [--repeat N] [--sink <folder>]
          marquee sensors [--sensors lhm|hwinfo]
        """);
    return 2;
}
