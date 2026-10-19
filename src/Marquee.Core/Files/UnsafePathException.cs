namespace Marquee.Core.Files;

/// A write or folder refused because something on its path could redirect it or let other programs change it.
/// The message says what and where, in words a user can act on.
public sealed class UnsafePathException(string message) : IOException(message);
