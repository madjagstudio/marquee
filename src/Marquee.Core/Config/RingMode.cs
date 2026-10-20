namespace Marquee.Core.Config;

/// What config.yaml's `ring:` says about the screen's LED ring. Theme lets each theme's `ring:` block (or the soft white
/// a theme without one gets) drive it; Off keeps it dark; Color shows MarqueeConfig.RingColor for every theme.
public enum RingMode { Theme, Off, Color }
