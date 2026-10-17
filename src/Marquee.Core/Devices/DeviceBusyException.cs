namespace Marquee.Core.Devices;

/// The device exists but another program (usually L-Connect 3) holds it open.
public sealed class DeviceBusyException(string message) : Exception(message);
