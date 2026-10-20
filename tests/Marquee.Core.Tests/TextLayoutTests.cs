using Marquee.Core.Rendering;

namespace Marquee.Core.Tests;

/// Where text and value widgets break their text into lines, and how many lines their box shows. Each character is
/// 10 px wide here, so the widths are easy to follow.
public class TextLayoutTests
{
    static float Measure(string text) => text.Length * 10;

    /// With `wrap: true`.
    static List<string> Lines(string text, float width) => TextLayout.Lines(text, width, wrap: true, Measure);

    static List<string> Unwrapped(string text, float width) => TextLayout.Lines(text, width, wrap: false, Measure);

    [Fact]
    public void Text_that_fits_stays_one_line_exactly_as_written()
    {
        Assert.Equal(["  two  spaces "], Lines("  two  spaces ", 140));
    }

    [Fact]
    public void Without_wrap_text_too_wide_for_its_box_stays_one_line()
    {
        Assert.Equal(["CPU temp is fine"], Unwrapped("CPU temp is fine", 90));
    }

    [Fact]
    public void Without_wrap_line_breaks_still_start_new_lines()
    {
        Assert.Equal(["CPU temp", "is fine"], Unwrapped("CPU temp\nis fine\n", 10));
    }

    [Fact]
    public void Long_text_wraps_at_spaces()
    {
        Assert.Equal(["CPU temp", "is fine"], Lines("CPU temp is fine", 90));
    }

    [Fact]
    public void A_line_keeps_its_own_spaces_and_drops_the_ones_it_breaks_at()
    {
        Assert.Equal(["a  b", "c"], Lines("a  b   c", 40));
    }

    [Fact]
    public void Line_breaks_start_new_lines_and_keep_blank_ones()
    {
        Assert.Equal(["one", "", "two"], Lines("one\n\ntwo", 1000));
        Assert.Equal(["one", "two"], Lines("one\r\ntwo", 1000));
    }

    [Fact]
    public void A_trailing_line_break_adds_no_blank_line()
    {
        // A `|` block scalar ends its text with a line break; `|-` doesn't. Both lay out the same.
        Assert.Equal(["one", "two"], Lines("one\ntwo\n", 1000));
    }

    [Fact]
    public void A_word_longer_than_the_line_gets_a_line_of_its_own_and_is_not_broken()
    {
        Assert.Equal(["a", "temperature", "b"], Lines("a temperature b", 50));
        Assert.Equal(["100°C"], Lines("100°C", 30));
    }

    [Fact]
    public void Each_paragraph_wraps_on_its_own()
    {
        Assert.Equal(["aa bb", "cc", "dd"], Lines("aa bb cc\ndd", 50));
    }

    [Theory]
    [InlineData(3, 10, 12, 100, 3)]
    [InlineData(3, 10, 12, 32, 3)]
    [InlineData(3, 10, 12, 31.9, 2)]
    [InlineData(3, 10, 12, 22, 2)]
    [InlineData(3, 10, 12, 21, 1)]
    [InlineData(3, 10, 12, 5, 1)]
    [InlineData(3, 10, 12, -4, 1)]
    [InlineData(1, 10, 12, 0, 1)]
    public void The_box_shows_the_lines_that_fit_and_always_the_first(int count, float advance, float extent, float height, int shown)
    {
        Assert.Equal(shown, TextLayout.Fitting(count, advance, extent, height));
    }
}
