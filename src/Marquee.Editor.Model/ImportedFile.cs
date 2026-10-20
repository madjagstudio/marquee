namespace Marquee.Editor.Model;

/// The kinds of file the editor imports into a theme, each into its own folder (AssetImport.FolderFor).
public enum AssetKind { Image, Font, Video }

/// A file imported into the theme. Path is what layout.yaml names it by: relative to the theme folder, with forward
/// slashes, as themes are written (`images/logo.png`). Warning says what a file that was taken will do, such as a clip
/// too slow to play on this PC, or is null. Copied is false for a file already in the theme folder, used where it is.
public sealed record ImportedFile(string Path, AssetKind Kind, string? Warning, bool Copied);

/// A file the import refused. Reason says why in a sentence for the user, such as "It could not be read as a font."
public sealed class AssetImportException(string reason, Exception? inner = null) : Exception(reason, inner)
{
    public string Reason => Message;
}
