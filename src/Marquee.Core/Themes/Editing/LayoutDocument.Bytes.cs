using System.Text;

namespace Marquee.Core.Themes.Editing;

/// The file as bytes, as the editor reads and saves it, so a save writes back the file's own bytes for everything
/// it didn't change: its byte order mark, and no U+FFFD where decoding met bytes that weren't UTF-8.
public sealed partial class LayoutDocument
{
    /// UTF-8 that throws on bytes it can't decode and text it can't encode, rather than writing U+FFFD in their place.
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// Opens a layout.yaml from its bytes, as File.ReadAllBytes reads them: UTF-8, with or without a byte order mark,
    /// which Text keeps and ToBytes writes back. Throws LayoutEditException as Open does, and for bytes that aren't
    /// UTF-8 text: a UTF-16 or UTF-32 file (by its byte order mark, or a zero byte, which no YAML text holds), or bytes
    /// no UTF-8 text has, which File.ReadAllText would turn into U+FFFD and the first save would write over.
    public static LayoutDocument FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])
            || bytes.IndexOf((byte)0) >= 0)
        {
            throw new LayoutEditException(
                "The file is UTF-16 or UTF-32 text, which the editor doesn't edit. Save it as UTF-8 in a text editor.");
        }

        try
        {
            return Open(StrictUtf8.GetString(bytes));
        }
        catch (DecoderFallbackException ex)
        {
            var at = Math.Clamp(ex.Index, 0, bytes.Length);
            throw new LayoutEditException(
                "The file holds bytes that aren't UTF-8 text, which a save would replace. Save it as UTF-8 in a text editor.",
                bytes[..at].Count((byte)'\n') + 1);
        }
    }

    /// The file's text as UTF-8 bytes, with its byte order mark when it had one: what the editor saves.
    public byte[] ToBytes() => StrictUtf8.GetBytes(Text);
}
