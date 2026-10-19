using Marquee.Core.Devices;

namespace Marquee.Host;

/// The screen came up busy: who holds it, what `lconnect.handover` says to do (Prompt: show the tray balloon that
/// offers to take over; TakeOver: the engine is doing it), and whether a handover could take it at all. CanTakeOver is
/// false when L-Connect's service isn't what holds the screen, and, while it is, when Marquee isn't elevated, because
/// stopping the service takes administrator rights; the action is then None, and the engine has logged why. A tray
/// offers its "Take over" item only while CanTakeOver is true.
public sealed record ScreenBusyInfo(LConnectState LConnect, HandoverAction Action, bool CanTakeOver);
