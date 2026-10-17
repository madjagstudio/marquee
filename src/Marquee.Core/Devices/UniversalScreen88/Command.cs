namespace Marquee.Core.Devices.UniversalScreen88;

/// Command bytes understood by the Universal Screen 8.8" firmware.
public enum Command : byte
{
    GetVer = 0x0A,
    Brightness = 0x0E,
    FrameRate = 0x0F,
    SetClock = 0x33,
    StopClock = 0x34,
    PushJpg = 0x65,
    PushPng = 0x66,
    StopPlay = 0x7B,
}
