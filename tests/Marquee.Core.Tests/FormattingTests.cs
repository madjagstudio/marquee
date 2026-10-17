using Marquee.Core.Expressions;
using Marquee.Core.Sensors;

namespace Marquee.Core.Tests;

public class FormattingTests
{
    [Theory]
    [InlineData(0, "0s")]
    [InlineData(59.9, "59s")]
    [InlineData(60, "1m")]
    [InlineData(8040, "2h 14m")]
    [InlineData(3600, "1h 0m")]
    [InlineData(90061, "1d 1h")]
    [InlineData(-5, "0s")]
    public void Duration(double seconds, string expected) => Assert.Equal(expected, Formatting.Duration(seconds));

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(107374182400, "100 GB")]
    [InlineData(-1, "0 B")]
    public void Bytes(double bytes, string expected) => Assert.Equal(expected, Formatting.Bytes(bytes));

    [Theory]
    [InlineData(null, 72.456, "72.46")]
    [InlineData("{0:0.0}°C", 72.26, "72.3°C")]
    [InlineData("{0:0}%", 99.6, "100%")]
    [InlineData("bytes", 1536, "1.5 KB")]
    [InlineData("duration", 8040, "2h 14m")]
    public void Apply_formats_numbers(string? format, double value, string expected) =>
        Assert.Equal(expected, Formatting.Apply(format, SensorValue.Of(value)));

    [Theory]
    [InlineData(null)]
    [InlineData("{0:0.0}")]
    [InlineData("bytes")]
    public void Apply_shows_strings_as_they_are(string? format) =>
        Assert.Equal("idle", Formatting.Apply(format, SensorValue.Of("idle")));

    [Fact]
    public void Numbers_ignore_the_current_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.Equal("72.5°C", Formatting.Apply("{0:0.0}°C", SensorValue.Of(72.5)));
            Assert.Equal("1.5 KB", Formatting.Bytes(1536));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("{0:0.0}°C", true)]
    [InlineData("duration", true)]
    [InlineData("°C", false)]
    [InlineData("{0:0.0", false)]
    [InlineData("{1}", false)]
    public void FormatError_accepts_composite_formats_and_helpers(string format, bool valid) =>
        Assert.Equal(valid, Formatting.FormatError(format) is null);
}
