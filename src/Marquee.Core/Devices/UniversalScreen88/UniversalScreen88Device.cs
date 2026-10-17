using System.Text;
using Marquee.Core.Usb;
using SkiaSharp;

namespace Marquee.Core.Devices.UniversalScreen88;

/// The 8.8" Universal Screen (USB 1CBE:A088): 1920×480 landscape canvas,
/// sent as 480×1920 portrait JPEGs behind encrypted command headers.
public sealed class UniversalScreen88Device : IDisplayDevice
{
    public const ushort VendorId = 0x1CBE;
    public const ushort ProductId = 0xA088;

    const int InitReplyTimeoutMs = 2000;
    const int FrameReplyTimeoutMs = 200;
    const int DrainTimeoutMs = 10;
    const int MaxReplyLength = 512;

    readonly Func<IUsbTransport> _openTransport;
    readonly Rotation _rotation;
    readonly HeaderBuilder _headers;
    readonly Func<DateTime> _now;
    IUsbTransport? _usb;

    public UniversalScreen88Device(
        Func<IUsbTransport> openTransport,
        Rotation rotation = Rotation.Clockwise,
        HeaderBuilder? headers = null,
        Func<DateTime>? now = null)
    {
        _openTransport = openTransport;
        _rotation = rotation;
        _headers = headers ?? new HeaderBuilder();
        _now = now ?? (() => DateTime.Now);
    }

    public DeviceInfo Info { get; } = new("Universal Screen 8.8\"", FrameEncoder.PanelHeight, FrameEncoder.PanelWidth);

    public string? FirmwareVersion { get; private set; }

    public void Open()
    {
        _usb = _openTransport();

        Send(Command.StopPlay);
        Thread.Sleep(150);
        if (Send(Command.GetVer) is { Body.Length: > 0 } version)
            FirmwareVersion = ReadAscii(version.Body);
        Send(Command.FrameRate, CommandParams.Byte(120));
        Send(Command.Brightness, CommandParams.Byte(100));
        Send(Command.SetClock, CommandParams.Clock(_now()));
        Send(Command.StopClock, CommandParams.Byte(0));

        using var transparent = FrameEncoder.Blank(SKColors.Transparent);
        SendWithPayload(Command.PushPng, FrameEncoder.EncodePng(transparent), InitReplyTimeoutMs);
        using var black = FrameEncoder.Blank(SKColors.Black);
        SendWithPayload(Command.PushJpg, FrameEncoder.EncodeJpeg(black, quality: 95), InitReplyTimeoutMs);
    }

    public void PushFrame(SKImage frame)
    {
        EnsureOpen();
        using var panel = FrameEncoder.ToPanel(frame, _rotation);
        SendWithPayload(Command.PushJpg, FrameEncoder.EncodeJpeg(panel), FrameReplyTimeoutMs);
    }

    public void SetBrightness(int percent) =>
        Send(Command.Brightness, CommandParams.Byte(Math.Clamp(percent, 0, 100)));

    public void Close()
    {
        _usb?.Dispose();
        _usb = null;
    }

    public void Dispose() => Close();

    Reply? Send(Command command, byte[]? parameters = null) =>
        Exchange(_headers.Build(command, parameters), InitReplyTimeoutMs);

    Reply? SendWithPayload(Command command, byte[] payload, int replyTimeoutMs)
    {
        var header = _headers.Build(command, CommandParams.Length(payload.Length));
        var packet = new byte[header.Length + payload.Length];
        header.CopyTo(packet, 0);
        payload.CopyTo(packet, header.Length);
        return Exchange(packet, replyTimeoutMs);
    }

    Reply? Exchange(byte[] packet, int replyTimeoutMs)
    {
        var usb = EnsureOpen();
        var buffer = new byte[MaxReplyLength];

        // Discard anything left over from a previous host or an unread reply.
        for (var i = 0; i < 8 && usb.Read(buffer, DrainTimeoutMs) > 0; i++) { }

        usb.Write(packet, Math.Max(2000, packet.Length / 500 + 2000));
        var count = usb.Read(buffer, replyTimeoutMs);
        return Reply.Parse(buffer.AsSpan(0, count));
    }

    IUsbTransport EnsureOpen() => _usb ?? throw new InvalidOperationException("Device is not open.");

    static string ReadAscii(byte[] body)
    {
        var end = Array.IndexOf(body, (byte)0);
        return Encoding.ASCII.GetString(body, 0, end < 0 ? body.Length : end);
    }
}
