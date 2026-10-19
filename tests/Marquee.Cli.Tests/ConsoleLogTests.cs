using System.Globalization;

namespace Marquee.Cli.Tests;

public class ConsoleLogTests
{
    [Fact]
    public void The_time_reads_the_same_in_every_culture()
    {
        // In a custom format ":" means the culture's time separator, which some cultures write as ".".
        var dotted = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        dotted.DateTimeFormat.TimeSeparator = ".";
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = dotted;
        try
        {
            Assert.Equal("[09:05:07] Started.", ConsoleLog.Line(new DateTime(2026, 10, 18, 9, 5, 7), "Started."));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
