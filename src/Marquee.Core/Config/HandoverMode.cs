namespace Marquee.Core.Config;

/// What config.yaml's `lconnect.handover` says to do when L-Connect has the screen. Off leaves it with L-Connect; Ask
/// shows a tray notification that takes the screen when clicked; Auto takes it at start, and whenever the screen comes
/// back busy. Taking it stops L-Connect's services only while the screen is opened, then starts them again.
public enum HandoverMode { Off, Ask, Auto }
