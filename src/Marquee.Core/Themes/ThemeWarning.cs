namespace Marquee.Core.Themes;

/// A problem found while loading a theme's assets. `marquee run` shows the message only (LoadedTheme.Warnings);
/// `marquee validate` also needs to know which file it is about and how serious it is, without reading the message.
/// File is the path as layout.yaml gives it, or null when the problem isn't about a file (a font family). Skipped means
/// the image, font file or layer isn't used at all, which validate reports as an error; otherwise the theme runs as
/// written on this PC (an unused shader binding) or may on another (a font family that isn't installed here).
internal sealed record ThemeWarning(string Message, string? File, bool Skipped);
