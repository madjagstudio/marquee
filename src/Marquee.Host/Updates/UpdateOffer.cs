namespace Marquee.Host.Updates;

/// A newer release the tray can offer: its version, its title and page on GitHub when the release has them (for "What's
/// new"), and the two files to download.
public sealed record UpdateOffer(Version Version, string? Title, Uri? ReleasePage, ReleaseAsset Installer, ReleaseAsset Signature);

/// One file of a release: its name, the size the release says it has, and where GitHub serves it.
public sealed record ReleaseAsset(string Name, long Size, Uri DownloadUrl);
