using System.Globalization;
using System.Text;
using Marquee.Core.Themes;

namespace Marquee.Core.Rendering;

/// A style's `case`, applied to text once its template is filled in, so a sensor's value changes case with the words
/// round it. It follows the culture's rules, so Turkish upper-cases i to İ.
static class TextCase
{
    /// `culture` is the renderer's (RenderContext.Locale), the PC's regional format unless a test pins it, the same
    /// culture the `time` helper writes names in. `before` is the text ahead of this on its line, such as the spans
    /// before a span: title case starts a word only where it ends in white space, so a word split between spans keeps
    /// one capital.
    public static string Apply(string text, LetterCase? letterCase, CultureInfo culture, string before = "") =>
        (letterCase ?? StyleDefaults.Case) switch
        {
            LetterCase.Upper => culture.TextInfo.ToUpper(text),
            LetterCase.Lower => culture.TextInfo.ToLower(text),
            LetterCase.Title => Title(text, culture.TextInfo, Waiting(before)),
            _ => text,
        };

    /// The first letter of each word in upper case, and the rest as written, so "gpu at 5.2 GHz" reads "Gpu At 5.2 GHz"
    /// rather than losing the capitals it had, as TextInfo.ToTitleCase would. A word is what lies between white space.
    /// Signs before its first letter are passed over, so "(gpu)" reads "(Gpu)", but a word that starts with a digit is
    /// left alone: "1st" stays "1st". `waiting` is whether the text starts where a word could.
    static string Title(string text, TextInfo info, bool waiting)
    {
        var title = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length;)
        {
            // A letter with its combining marks, or a surrogate pair, is one element and changes case as one.
            var length = StringInfo.GetNextTextElementLength(text, i);
            var element = text.Substring(i, length);
            var capital = waiting && char.IsLetter(text, i);
            waiting = Next(text, i, waiting);
            title.Append(capital ? info.ToUpper(element) : element);
            i += length;
        }

        return title.ToString();
    }

    /// Whether a word could start after `text`: after white space, and after signs that follow it, but not after a
    /// letter or a digit.
    static bool Waiting(string text)
    {
        var waiting = true;
        for (var i = 0; i < text.Length; i += StringInfo.GetNextTextElementLength(text, i))
            waiting = Next(text, i, waiting);
        return waiting;
    }

    /// Whether a word could start after the element at `i`.
    static bool Next(string text, int i, bool waiting) =>
        char.IsWhiteSpace(text, i) || (waiting && !char.IsLetterOrDigit(text, i));
}
