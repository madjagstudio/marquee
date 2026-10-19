namespace Marquee.Host;

/// A theme the engine just loaded and is about to show: its name and its warnings (a missing image, a shader that
/// didn't compile), for a host's banner. The engine has logged the warnings already.
public sealed record LoadedThemeInfo(string Name, IReadOnlyList<string> Warnings);
