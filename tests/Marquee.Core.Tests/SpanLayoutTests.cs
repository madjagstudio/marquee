using Marquee.Core.Rendering;
using static Marquee.Core.Rendering.SpanLayout;

namespace Marquee.Core.Tests;

/// Where a text widget's spans break into lines, how many lines its box shows and where their baselines go. Each
/// character is 10 px wide here, whatever its span, so the widths are easy to follow.
public class SpanLayoutTests
{
    static float Measure(int span, string text) => text.Length * 10;

    static List<List<Piece>> Lines(float width, bool wrap, params string[] texts) => SpanLayout.Lines(texts, width, wrap, Measure);

    [Fact]
    public void Spans_that_fit_are_one_line()
    {
        Assert.Equal([[new(0, "52"), new(1, "°C")]], Lines(100, wrap: true, "52", "°C"));
    }

    [Fact]
    public void A_line_break_in_a_span_starts_a_new_line_and_the_next_span_continues_it()
    {
        Assert.Equal([[new(0, "a")], [new(0, "b"), new(1, "c")]], Lines(100, wrap: false, "a\nb", "c"));
    }

    [Fact]
    public void A_line_break_at_a_spans_end_starts_the_next_span_on_a_new_line()
    {
        Assert.Equal([[new(0, "Fan")], [new(1, "1450")]], Lines(100, wrap: false, "Fan\n", "1450"));
    }

    [Fact]
    public void Line_breaks_at_the_end_add_no_blank_lines()
    {
        Assert.Equal([[new(0, "a"), new(1, "b")]], Lines(100, wrap: false, "a", "b\n\r\n"));
    }

    /// A blank line keeps the spans it is in, empty, so it still has their height.
    [Fact]
    public void A_blank_line_keeps_its_spans()
    {
        Assert.Equal([[new(0, "a")], [new(0, ""), new(1, "")], [new(1, "b")]], Lines(100, wrap: false, "a\n", "\nb"));
    }

    [Fact]
    public void Without_wrap_a_line_too_wide_stays_one_line()
    {
        Assert.Equal([[new(0, "CPU "), new(1, "64°C"), new(2, " running hot")]], Lines(100, wrap: false, "CPU ", "64°C", " running hot"));
    }

    [Fact]
    public void Wrapping_breaks_at_spaces_across_spans_and_drops_the_spaces_at_a_break()
    {
        Assert.Equal(
            [[new(0, "CPU "), new(1, "64°C")], [new(2, "running")], [new(2, "hot")]],
            Lines(100, wrap: true, "CPU ", "64°C", " running hot"));
    }

    /// A word split between spans, such as a number and its unit, is one word: it stays together.
    [Fact]
    public void A_word_across_spans_is_not_broken()
    {
        Assert.Equal([[new(0, "52"), new(1, "°C")], [new(1, "now")]], Lines(30, wrap: true, "52", "°C now"));
    }

    /// Letter spacing follows every character but a line's last, so a join between spans has its first span's spacing,
    /// as the same characters in one span would.
    [Fact]
    public void Wrapping_counts_the_spacing_at_a_join()
    {
        // "ab cd" is 50 px, with 5 px after each character but the last: 70 px, in one span or in two.
        static float Spaced(int span, string text) => text.Length * 10 + 5 * Math.Max(0, text.Length - 1);

        Assert.Equal([[new(0, "ab"), new(1, " cd")]], SpanLayout.Lines(["ab", " cd"], 70, wrap: true, Spaced, _ => 5));
        Assert.Equal([[new(0, "ab")], [new(1, "cd")]], SpanLayout.Lines(["ab", " cd"], 69, wrap: true, Spaced, _ => 5));
    }

    [Fact]
    public void Spaces_too_wide_for_the_box_wrap_to_a_blank_line()
    {
        Assert.Equal([[new(0, ""), new(1, "")]], Lines(20, wrap: true, "   ", "  "));
    }

    [Fact]
    public void Wrapping_follows_text_layouts_rules()
    {
        // The same text as one span wraps exactly as plain text does.
        const string Text = "  indented text that wraps   at spaces  ";
        var plain = TextLayout.Lines(Text, 90, wrap: true, s => s.Length * 10);
        Assert.Equal(plain, Lines(90, wrap: true, Text).Select(line => string.Concat(line.Select(p => p.Text))));
    }

    static readonly LineBox Small = new(Height: 36, Ascent: -24, Descent: 6);
    static readonly LineBox Big = new(Height: 108, Ascent: -72, Descent: 18);

    [Fact]
    public void Lines_of_one_size_sit_where_plain_texts_lines_do()
    {
        // Plain text: the first baseline is the middle less half the letters' extent and half the other lines' advance.
        var baselines = Baselines([Small, Small, Small], 100);

        Assert.Equal([100 + 9 - 36, 100 + 9, 100 + 9 + 36], baselines);
    }

    [Fact]
    public void Each_line_is_centred_in_a_box_as_tall_as_its_largest_span()
    {
        // Boxes of 36 and 108 stack from 100 - 72 = 28: the first from 28 to 64, the second from 64 to 172. Each baseline
        // sits where the letters are centred in their box.
        var baselines = Baselines([Small, Big], 100);

        Assert.Equal([46 + 9, 118 + 27], baselines);
    }

    [Fact]
    public void Lines_past_the_last_that_fits_whole_are_left_out()
    {
        // From the top of the first line's letters to the bottom of the last's: the boxes' heights, less the room above the
        // first line's letters (3 px in a small box) and below the last's (9 px in a big one). Two small lines take
        // 72 - 3 - 3 = 66 px, and a big line after them 180 - 3 - 9 = 168.
        Assert.Equal(1, Fitting([Small, Small], 65));
        Assert.Equal(2, Fitting([Small, Small], 66));
        Assert.Equal(2, Fitting([Small, Small, Big], 167));
        Assert.Equal(3, Fitting([Small, Small, Big], 168));
    }

    [Fact]
    public void The_first_line_always_shows()
    {
        Assert.Equal(1, Fitting([Big, Small], 10));
    }
}
