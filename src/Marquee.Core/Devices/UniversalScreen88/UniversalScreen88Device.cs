using System.Text;
using Marquee.Core.Logging;
using Marquee.Core.Usb;
using SkiaSharp;

namespace Marquee.Core.Devices.UniversalScreen88;

/// The 8.8" Universal Screen (USB 1CBE:A088): a 480×1920 or 1920×480 canvas depending on mount orientation,
/// sent as 480×1920 panel JPEGs behind encrypted command headers. A frame identical to the last one sent is skipped
/// (see RepeatedFrames), because the panel conversion and JPEG encode cost more than drawing most themes.
public sealed class UniversalScreen88Device : IDisplayDevice
{
    public const ushort VendorId = 0x1CBE;
    public const ushort ProductId = 0xA088;

    const int InitReplyTimeoutMs = 2000;
    const int FrameReplyTimeoutMs = 200;
    const int DrainTimeoutMs = 10;
    const int MaxReplyLength = 512;

    readonly Func<IUsbTransport> _openTransport;
    readonly Orientation _orientation;
    readonly HeaderBuilder _headers;
    readonly Func<DateTime> _now;
    readonly RepeatedFrames _repeats;
    readonly ILog _log;
    IUsbTransport? _usb;
    WicJpeg? _jpeg; // made by the first frame after Open and let go by Close, both on the thread that pushes frames

    public UniversalScreen88Device(
        Func<IUsbTransport> openTransport,
        Orientation orientation = Orientation.Landscape,
        HeaderBuilder? headers = null,
        Func<DateTime>? now = null,
        TimeProvider? time = null,
        ILog? log = null)
    {
        _openTransport = openTransport;
        _orientation = orientation;
        var canvas = orientation.CanvasSize();
        Info = new("Universal Screen 8.8\"", canvas.Width, canvas.Height);
        _headers = headers ?? new HeaderBuilder();
        _now = now ?? (() => DateTime.Now);
        _repeats = new RepeatedFrames(time);
        _log = log ?? NullLog.Instance;
    }

    public DeviceInfo Info { get; }

    public string? FirmwareVersion { get; private set; }

    public void Open()
    {
        Close();
        FirmwareVersion = null;
        _repeats.Forget(); // Open clears the panel to black
        _usb = _openTransport();

        try
        {
            Send(Command.StopPlay);
            Thread.Sleep(150);
            if (Send(Command.GetVer) is { Command: (byte)Command.GetVer, Ok: true, Body.Length: > 0 } version)
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
        catch
        {
            Close();
            throw;
        }
    }

    public void PushFrame(SKImage frame)
    {
        EnsureOpen();
        if (_repeats.IsRepeat(frame)) return;
        _jpeg ??= new WicJpeg(_log);
        var reply = SendWithPayload(Command.PushJpg, FrameEncoder.EncodePanel(frame, _orientation, _jpeg), FrameReplyTimeoutMs);

        // A frame the screen refused goes again, even if the next one is identical. No reply at all still counts as
        // sent, so the skip doesn't rest on every frame being answered in time; the refresh covers a frame lost that way.
        if (reply is not { Ok: false }) _repeats.Sent(frame);
    }

    public void SetBrightness(int percent) =>
        Send(Command.Brightness, CommandParams.Byte(Math.Clamp(percent, 0, 100)));

    public void Close()
    {
        _usb?.Dispose();
        _usb = null;
        _jpeg?.Dispose();
        _jpeg = null;
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
