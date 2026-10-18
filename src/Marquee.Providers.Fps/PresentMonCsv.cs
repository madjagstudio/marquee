using System.Globalization;

namespace Marquee.Providers.Fps;

/// One presented frame from PresentMon's CSV. StartMs is when the CPU started the frame, in ms since PresentMon started
/// recording, or null when the CSV has no start-time column. IntervalMs is the time since the process's previous
/// Present on the same swap chain. SwapChain is empty when the CSV has no swap chain column.
public sealed record PresentMonFrame(string Application, int ProcessId, string SwapChain, double? StartMs, double IntervalMs);

/// Where the columns Marquee reads sit in one PresentMon CSV, found by name from its header line. A column that isn't
/// there is -1. StartScale turns the start column into milliseconds (1000 for 1.x's TimeInSeconds).
public sealed record PresentMonHeader(int FieldCount, int Application, int ProcessId, int SwapChain, int Interval, int Start,
    double StartScale);

/// Reads the CSV that `PresentMon --output_stdout` writes, one row per presented frame of every process. Columns are
/// found by header name, never by position, so a column added or moved in a later PresentMon doesn't break it.
/// PresentMon doesn't quote fields, so there is no CSV quoting here: a row with a quoted comma has the wrong number of
/// fields and is skipped, and a quoted number doesn't parse and is skipped.
public static class PresentMonCsv
{
    /// Builds the column map from the header line. Names match without regard to case because PresentMon 1.x (and 2.x
    /// with --v1_metrics) spells them "msBetweenPresents" where 2.x writes "MsBetweenPresents". The frame interval is
    /// MsBetweenPresents, else MsBetweenAppStart; the start time is CPUStartTime (ms), else TimeInSeconds (seconds).
    /// The other start-time columns (CPUStartQPC, CPUStartQPCTime, CPUStartDateTime) come from flags Marquee doesn't
    /// pass and are not read; rows then have no start time.
    public static PresentMonHeader ParseHeader(string line)
    {
        var names = line.TrimStart('﻿').TrimEnd('\r', '\n').Split(',');
        int Find(string name) => Array.FindIndex(names, n => string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase));

        var processId = Find("ProcessID");
        if (processId < 0)
            throw new FormatException("PresentMon's CSV header has no ProcessID column, so frames can't be told apart by process.");

        var interval = Find("MsBetweenPresents");
        if (interval < 0) interval = Find("MsBetweenAppStart");
        if (interval < 0)
            throw new FormatException("PresentMon's CSV header has neither a MsBetweenPresents nor a MsBetweenAppStart column, " +
                "so there is no frame time to read.");

        var start = Find("CPUStartTime");
        var startScale = 1.0;
        if (start < 0 && (start = Find("TimeInSeconds")) >= 0) startScale = 1000;

        return new PresentMonHeader(names.Length, Find("Application"), processId, Find("SwapChainAddress"), interval, start, startScale);
    }

    /// Reads one data row. Returns false, without throwing, for a row that can't be used: the wrong number of fields,
    /// a process id that isn't a whole number, an interval that is "NA", not a finite number, or not positive, or a
    /// start time that isn't a finite number.
    public static bool TryParseRow(string line, PresentMonHeader header, out PresentMonFrame frame)
    {
        frame = null!;
        var fields = line.TrimEnd('\r', '\n').Split(',');
        if (fields.Length != header.FieldCount) return false;

        if (!int.TryParse(fields[header.ProcessId], NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId)) return false;
        if (!TryNumber(fields[header.Interval], out var interval) || interval <= 0) return false;

        double? start = null;
        if (header.Start >= 0)
        {
            if (!TryNumber(fields[header.Start], out var value)) return false;
            start = value * header.StartScale;
        }

        frame = new PresentMonFrame(Field(fields, header.Application), processId, Field(fields, header.SwapChain), start, interval);
        return true;
    }

    static string Field(string[] fields, int index) => index >= 0 ? fields[index].Trim() : "";

    // "NA" (PresentMon's "no value"), "NaN" and values too big for a double all fail here.
    static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
