using Marquee.Core.Devices;
using Marquee.Core.Devices.UniversalScreen88;
using Marquee.Core.Usb;
using SkiaSharp;

return args switch
{
    ["devices"] => ListDevices(),
    ["show-image", var file, .. var options] => ShowImage(file, options),
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
    var rotation = Rotation.Clockwise;
    int? brightness = null;
    string? sink = null;

    for (var i = 0; i < options.Length; i++)
    {
        var value = i + 1 < options.Length ? options[i + 1] : null;
        switch (options[i])
        {
            case "--rotation" when value is "cw": rotation = Rotation.Clockwise; i++; break;
            case "--rotation" when value is "ccw": rotation = Rotation.CounterClockwise; i++; break;
            case "--brightness" when int.TryParse(value, out var b): brightness = b; i++; break;
            case "--sink" when value is not null: sink = value; i++; break;
            default: return Usage();
        }
    }

    using var image = SKImage.FromEncodedData(file);
    if (image is null)
    {
        Console.Error.WriteLine($"Could not read image: {file}");
        return 1;
    }

    IDisplayDevice? device = sink is not null ? new FileSinkDevice(sink) : FindScreen(rotation);
    if (device is null)
    {
        Console.Error.WriteLine("No supported screen found. Run `marquee devices` to check.");
        return 1;
    }

    try
    {
        device.Open();
        if (brightness is { } level) device.SetBrightness(level);
        device.PushFrame(image);
        var firmware = device is UniversalScreen88Device screen && screen.FirmwareVersion is { } v ? $" (firmware {v})" : "";
        Console.WriteLine($"Sent {file} to {device.Info.Name}{firmware}.");
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

static IDisplayDevice? FindScreen(Rotation rotation) =>
    WinUsbTransport.Enumerate()
        .Where(e => DeviceRegistry.IsSupported(e.VendorId, e.ProductId))
        .Select(e => DeviceRegistry.Create(e, WinUsbTransport.Open, rotation))
        .FirstOrDefault();

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          marquee devices
          marquee show-image <file> [--rotation cw|ccw] [--brightness 0-100] [--sink <folder>]
        """);
    return 2;
}
