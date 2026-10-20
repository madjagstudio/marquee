namespace Marquee.Editor.Model.Values;

/// Where a key's value on the canvas comes from, highest first: the test values panel shows it beside each key.
public enum ValueSourceKind
{
    /// A test value pinned in the panel: a number, stale or missing, or a time for `now`.
    Pinned,

    /// The tray's sensor feed.
    Tray,

    /// What the editor reads itself: the readable hardware sensors and the agent providers, or the PC's clock for
    /// `now`.
    Local,

    /// A believable stand-in from Samples, when nothing above has the key.
    Sample,
}
