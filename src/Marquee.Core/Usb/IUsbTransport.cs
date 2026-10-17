namespace Marquee.Core.Usb;

/// Bulk transfers to one opened USB device: OUT pipe 0x01, IN pipe 0x81.
public interface IUsbTransport : IDisposable
{
    void Write(byte[] data, int timeoutMs);

    /// Returns the number of bytes read, or 0 if nothing arrived before the timeout.
    int Read(byte[] buffer, int timeoutMs);
}
