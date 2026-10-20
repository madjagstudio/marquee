using System.Globalization;
using Marquee.Core.Rendering;
using Marquee.Core.Themes;

namespace Marquee.Core.Tests;

/// A style's `case`, applied to text after its template is filled in, by the rules of the renderer's culture.
public class TextCaseTests
{
    static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    [Theory]
    [InlineData(LetterCase.Upper, "GeForce rtx 4090 · 64°c", "GEFORCE RTX 4090 · 64°C")]
    [InlineData(LetterCase.Lower, "GeForce RTX 4090 · 64°C", "geforce rtx 4090 · 64°c")]
    [InlineData(LetterCase.None, "GeForce rtx 4090", "GeForce rtx 4090")]
    [InlineData(null, "GeForce rtx 4090", "GeForce rtx 4090")]
    public void Upper_lower_and_none(LetterCase? letterCase, string text, string expected) =>
        Assert.Equal(expected, TextCase.Apply(text, letterCase, English));

    [Theory]
    [InlineData("cpu temp", "Cpu Temp")]
    [InlineData("GPU at 5.2 GHz", "GPU At 5.2 GHz")]
    [InlineData("  two  spaces\nand a new line", "  Two  Spaces\nAnd A New Line")]
    [InlineData("(gpu) \"hot\" °c", "(Gpu) \"Hot\" °C")]
    [InlineData("1st place, 2nd fan", "1st Place, 2nd Fan")]
    [InlineData("don't stop-start", "Don't Stop-start")]
    [InlineData("élan ñu", "Élan Ñu")]
    [InlineData("", "")]
    public void Title_capitalises_the_first_letter_of_each_word_and_keeps_the_rest(string text, string expected) =>
        Assert.Equal(expected, TextCase.Apply(text, LetterCase.Title, English));

    /// A span's text follows the spans before it on its line: it starts a word only where they end in white space, or
    /// in signs after white space.
    [Theory]
    [InlineData("", "temp", "Temp")]
    [InlineData("cpu", "temp", "temp")]
    [InlineData("cpu ", "temp", "Temp")]
    [InlineData("cpu\n", "temp", "Temp")]
    [InlineData("at (", "hot)", "Hot)")]
    [InlineData("1", "st", "st")]
    public void Title_follows_the_text_before_it(string before, string text, string expected) =>
        Assert.Equal(expected, TextCase.Apply(text, LetterCase.Title, English, before));

    [Fact]
    public void Title_keeps_a_letter_and_its_combining_mark_together()
    {
        // "e" followed by a combining acute accent is one letter on screen.
        Assert.Equal("Éclair", TextCase.Apply("éclair", LetterCase.Title, English));
    }

    [Fact]
    public void The_cultures_rules_apply()
    {
        // Turkish has a dotted capital İ and a dotless small ı.
        Assert.Equal("İSTANBUL", TextCase.Apply("istanbul", LetterCase.Upper, Turkish));
        Assert.Equal("ıiı", TextCase.Apply("Iİı", LetterCase.Lower, Turkish));
        Assert.Equal("İzmir", TextCase.Apply("izmir", LetterCase.Title, Turkish));
        Assert.Equal("ISTANBUL", TextCase.Apply("istanbul", LetterCase.Upper, English));
    }

    [Fact]
    public void The_threads_culture_plays_no_part()
    {
        // The renderer passes its locale's culture, which tests pin: a Turkish test machine draws what any other does.
        Assert.Equal("ISTANBUL", InCulture("tr-TR", () => TextCase.Apply("istanbul", LetterCase.Upper, English)));
        Assert.Equal("İSTANBUL", InCulture("en-US", () => TextCase.Apply("istanbul", LetterCase.Upper, Turkish)));
    }

    static string InCulture(string name, Func<string> run)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        try
        {
            return run();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
