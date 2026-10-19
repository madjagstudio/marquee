using Marquee.Providers.Fps;

namespace Marquee.Providers.Fps.Tests;

public class PresentMonCsvTests
{
    // The column names are the ones PresentMon 2.6.0's console README lists for the default metrics, but their order
    // here is made up: the real header comes from running PresentMon, which needs admin rights. Parsing goes by name,
    // so the order doesn't matter; the hardware check confirms the real header.
    const string V2Header =
        "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode," +
        "CPUStartTime,MsBetweenSimulationStart,MsBetweenPresents,MsBetweenDisplayChange,MsInPresentAPI," +
        "MsRenderPresentLatency,MsUntilDisplayed,MsPCLatency,MsBetweenAppStart,MsCPUBusy,MsCPUWait,MsGPULatency," +
        "MsGPUTime,MsGPUBusy,MsGPUWait,DisplayLatency,DisplayedTime,MsAnimationError,AnimationTime," +
        "MsAllInputToPhotonLatency,MsClickToPhotonLatency";

    static string V2Row(string application = "game.exe", string pid = "4242", string start = "1234.5678",
        string betweenPresents = "16.6667", string betweenAppStart = "16.5000") =>
        $"{application},{pid},0x000001F2A3B4C5D0,DXGI,1,0,0,Composed: Flip,{start},NA,{betweenPresents},16.6667,0.1200," +
        $"3.4000,8.0000,NA,{betweenAppStart},4.0000,12.5000,1.0000,6.0000,5.5000,0.5000,20.0000,16.6667,0.0000," +
        "1234.5678,NA,NA";

    [Fact]
    public void Reads_a_frame_by_column_name()
    {
        var header = PresentMonCsv.ParseHeader(V2Header);

        Assert.True(PresentMonCsv.TryParseRow(V2Row(), header, out var frame));
        Assert.Equal(new PresentMonFrame("game.exe", 4242, "0x000001F2A3B4C5D0", 1234.5678, 16.6667), frame);
    }

    [Fact]
    public void Falls_back_to_the_time_between_app_starts()
    {
        var header = PresentMonCsv.ParseHeader("Application,ProcessID,SwapChainAddress,CPUStartTime,MsBetweenAppStart");

        Assert.True(PresentMonCsv.TryParseRow("game.exe,7,0x10,500.25,8.5", header, out var frame));
        Assert.Equal(new PresentMonFrame("game.exe", 7, "0x10", 500.25, 8.5), frame);
    }

    [Fact]
    public void Reads_the_v1_metrics_header()
    {
        // PresentMon 1.x (and 2.x with --v1_metrics) spells the interval with a lower-case "ms" and gives the start
        // time in seconds.
        var header = PresentMonCsv.ParseHeader(
            "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode," +
            "Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,msUntilRenderComplete,msUntilDisplayed,msBetweenDisplayChange");

        Assert.True(PresentMonCsv.TryParseRow("game.exe,99,0xABC,DXGI,1,0,0,Hardware: Independent Flip,0,2.5,0.1,6.9444,1.0,2.0,6.9444",
            header, out var frame));
        Assert.Equal(new PresentMonFrame("game.exe", 99, "0xABC", 2500, 6.9444), frame);
    }

    [Fact]
    public void Ignores_unknown_columns_wherever_they_are()
    {
        var header = PresentMonCsv.ParseHeader("SomethingNew,MsBetweenPresents,Extra,ProcessID,CPUStartTime,Application,Later");

        Assert.True(PresentMonCsv.TryParseRow("x,10,y,12,100,app.exe,z", header, out var frame));
        Assert.Equal(new PresentMonFrame("app.exe", 12, "", 100, 10), frame);
    }

    [Fact]
    public void Rows_have_no_start_time_when_the_header_has_none()
    {
        var header = PresentMonCsv.ParseHeader("Application,ProcessID,MsBetweenPresents");

        Assert.True(PresentMonCsv.TryParseRow("app.exe,12,10", header, out var frame));
        Assert.Null(frame.StartMs);
    }

    [Fact]
    public void Tolerates_a_byte_order_mark_and_line_ending_in_the_header()
    {
        var header = PresentMonCsv.ParseHeader("﻿ProcessID,MsBetweenPresents,Application\r");

        Assert.True(PresentMonCsv.TryParseRow("12,10,app.exe", header, out var frame));
        Assert.Equal("app.exe", frame.Application);
    }

    [Theory]
    [InlineData("Application,SwapChainAddress,CPUStartTime,MsBetweenPresents", "ProcessID")]
    [InlineData("Application,ProcessID,SwapChainAddress,CPUStartTime,DisplayedTime", "MsBetweenPresents")]
    [InlineData("", "ProcessID")]
    public void Refuses_a_header_without_the_needed_columns(string line, string missing)
    {
        var error = Assert.Throws<FormatException>(() => PresentMonCsv.ParseHeader(line));

        Assert.Contains(missing, error.Message);
    }

    [Fact]
    public void Skips_rows_whose_interval_is_NA()
    {
        var header = PresentMonCsv.ParseHeader(V2Header);

        Assert.False(PresentMonCsv.TryParseRow(V2Row(betweenPresents: "NA"), header, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("game.exe,4242")]
    [InlineData("not,a,presentmon,row")]
    [InlineData("\"game, the sequel.exe\",4242,0x1,DXGI,1,0,0,Composed: Flip,1.0,NA,16.6,16.6,0.1,3.4,8.0,NA,16.5,4.0,12.5,1.0,6.0,5.5,0.5,20.0,16.6,0.0,1.0,NA,NA")]
    public void Skips_malformed_rows(string line)
    {
        var header = PresentMonCsv.ParseHeader(V2Header);

        Assert.False(PresentMonCsv.TryParseRow(line, header, out _));
    }

    [Theory]
    [InlineData("abc", "1.0", "16.6")]
    [InlineData("", "1.0", "16.6")]
    [InlineData("4242", "1.0", "0")]
    [InlineData("4242", "1.0", "-16.6")]
    [InlineData("\"4242\"", "1.0", "16.6")]
    [InlineData("4242", "1.0", "NaN")]
    [InlineData("4242", "1.0", "Infinity")]
    [InlineData("4242", "1.0", "1e400")]
    [InlineData("4242", "NA", "16.6")]
    [InlineData("4242", "soon", "16.6")]
    public void Skips_rows_with_unusable_numbers(string pid, string start, string interval)
    {
        var header = PresentMonCsv.ParseHeader(V2Header);

        Assert.False(PresentMonCsv.TryParseRow(V2Row(pid: pid, start: start, betweenPresents: interval), header, out _));
    }

    [Fact]
    public void Reads_numbers_the_same_in_every_culture()
    {
        var header = PresentMonCsv.ParseHeader(V2Header);
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.True(PresentMonCsv.TryParseRow(V2Row(), header, out var frame));
            Assert.Equal(16.6667, frame.IntervalMs);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }
}
