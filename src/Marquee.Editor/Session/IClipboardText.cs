using System.Runtime.InteropServices;
using System.Windows;

namespace Marquee.Editor.Session;

/// The clipboard's text, behind an interface so tests don't touch the real one.
public interface IClipboardText
{
    /// The text on the clipboard; null when there is none, or it can't be read.
    string? GetText();

    /// Puts `text` on the clipboard. False when another program holds it.
    bool SetText(string text);
}

/// Windows' clipboard, through WPF. Another program may hold it open for a moment, which WPF reports as a COM error.
public sealed class WpfClipboard : IClipboardText
{
    public string? GetText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public bool SetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
