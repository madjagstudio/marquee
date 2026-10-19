namespace Marquee.Core.Video;

/// A clip that can't be played. The message is shown to the theme's author and says what's wrong with the clip.
public sealed class VideoDecodeException(string message, Exception? inner = null) : Exception(message, inner);
