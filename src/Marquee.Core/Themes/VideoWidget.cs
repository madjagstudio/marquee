namespace Marquee.Core.Themes;

/// A clip played inside a box, as a video layer plays one across the canvas: decoded ahead on its own thread, looping,
/// and fitted to the box like an image. Path is relative to the theme folder.
public sealed record VideoWidget(string Path, ImageFit Fit) : WidgetSpec;
