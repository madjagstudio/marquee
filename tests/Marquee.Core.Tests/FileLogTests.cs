using System.Text;
using Marquee.Core.Logging;

namespace Marquee.Core.Tests;

public class FileLogTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "marquee-logs-" + Guid.NewGuid().ToString("N")[..8]);
    DateTime _now = new(2026, 10, 18, 9, 5, 3);

    FileLog NewLog(long maxFileBytes = FileLog.DefaultMaxFileBytes) => new(_folder, () => _now, maxFileBytes);

    string LogFile(string day) => Path.Combine(_folder, $"marquee-{day}.log");

    /// Reads the way Notepad or the tray's "Open logs" would, so it works while the log still has the file open.
    static string[] ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    string Touch(string name)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "old");
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
            else if (File.Exists(_folder)) File.Delete(_folder);
        }
        catch (IOException)
        {
            // Something still has a file open; %TEMP% is cleaned eventually.
        }
    }

    [Fact]
    public void Lines_land_in_todays_file_in_the_consoles_format()
    {
        using (var log = NewLog())
        {
            log.Info("Theme loaded");
            log.Warn("Sensor stale");
            log.Error("Device lost");
        }

        Assert.Equal(
            new[] { "[09:05:03] Theme loaded", "[09:05:03] Warning: Sensor stale", "[09:05:03] Error: Device lost" },
            ReadLines(LogFile("20261018")));
        Assert.Equal((byte)'[', File.ReadAllBytes(LogFile("20261018"))[0]); // no BOM
    }

    [Fact]
    public void Text_is_utf8()
    {
        using (var log = NewLog()) log.Info("Température 45 °C");

        Assert.Equal("[09:05:03] Température 45 °C" + Environment.NewLine,
            Encoding.UTF8.GetString(File.ReadAllBytes(LogFile("20261018"))));
    }

    [Fact]
    public void Midnight_starts_a_new_file()
    {
        _now = new DateTime(2026, 10, 18, 23, 59, 59);
        using (var log = NewLog())
        {
            log.Info("before");
            _now = new DateTime(2026, 10, 19, 0, 0, 1);
            log.Info("after");
        }

        Assert.Equal(new[] { "[23:59:59] before" }, ReadLines(LogFile("20261018")));
        Assert.Equal(new[] { "[00:00:01] after" }, ReadLines(LogFile("20261019")));
    }

    [Fact]
    public void Opening_keeps_todays_file_and_the_six_days_before_it()
    {
        var tooOld = Touch("marquee-20261011.log");
        File.SetLastWriteTime(tooOld, _now); // judged by the name, not the file's time
        var oldest = Touch("marquee-20261012.log");
        File.SetLastWriteTime(oldest, new DateTime(2020, 1, 1));
        Touch("marquee-20261017.log");
        Touch("marquee-20261018.log");
        Touch("marquee-20250101.txt");
        Touch("marquee-notadate.log");
        Touch("marquee-20261399.log");
        Touch("marquee-2026101.log");
        Touch("other.log");

        using (var log = NewLog()) log.Info("hello");

        Assert.Equal(
            new[]
            {
                "marquee-20250101.txt", "marquee-2026101.log", "marquee-20261012.log", "marquee-20261017.log",
                "marquee-20261018.log", "marquee-20261399.log", "marquee-notadate.log", "other.log",
            },
            Directory.GetFiles(_folder).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void A_new_day_prunes_again()
    {
        Touch("marquee-20261012.log");
        using var log = NewLog();
        log.Info("today");
        Assert.True(File.Exists(LogFile("20261012")));

        _now = _now.AddDays(1);
        log.Info("tomorrow");

        Assert.False(File.Exists(LogFile("20261012")));
    }

    /// The tray and a `marquee run` write the same day's file. Each line must land after whatever is already there,
    /// whichever process wrote it, not where this log last left off.
    [Fact]
    public void Two_logs_on_one_folder_keep_every_line_in_the_order_written()
    {
        using (var tray = NewLog())
        using (var cli = NewLog())
        {
            tray.Info("tray 1");
            cli.Info("cli 1");
            cli.Info("cli 2");
            tray.Info("tray 2");
        }

        Assert.Equal(
            new[] { "[09:05:03] tray 1", "[09:05:03] cli 1", "[09:05:03] cli 2", "[09:05:03] tray 2" },
            ReadLines(LogFile("20261018")));
    }

    [Fact]
    public void Two_logs_writing_at_once_lose_no_lines()
    {
        using (var tray = NewLog())
        using (var cli = NewLog())
        {
            Parallel.For(0, 8, thread =>
            {
                var log = thread % 2 == 0 ? tray : cli;
                for (var i = 0; i < 200; i++) log.Info($"thread {thread} line {i}");
            });
        }

        var lines = ReadLines(LogFile("20261018"));
        Assert.Equal(1600, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^\[09:05:03\] thread \d line \d+$", line));
    }

    /// The cap is on the file, so it counts what the other log wrote too.
    [Fact]
    public void The_size_cap_counts_lines_another_log_wrote()
    {
        using var tray = NewLog(maxFileBytes: 60);
        using var cli = NewLog(maxFileBytes: 60);

        tray.Info("tray 1");                    // 19 bytes with the time and the newline
        cli.Info("cli 1, a little longer");     // 35, so 54 in all
        tray.Info("tray 2");                    // would make 73, so the notice instead

        var lines = ReadLines(LogFile("20261018"));
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("[09:05:03] Warning: The log reached its", lines[^1]);
    }

    [Fact]
    public void Reaching_the_size_cap_writes_one_notice_then_drops_lines_until_the_next_day()
    {
        Directory.CreateDirectory(_folder);
        using (var existing = File.Create(LogFile("20261018"))) existing.SetLength(FileLog.DefaultMaxFileBytes - 40);

        using (var log = NewLog())
        {
            log.Info("fits");                               // 17 bytes with the newline
            log.Info("this line would go past the limit");  // dropped, and the notice written instead
            log.Error("dropped too");
            _now = _now.AddDays(1);
            log.Info("a new day");
        }

        var tail = ReadLines(LogFile("20261018"))[^2..];
        Assert.Equal(
            new[] { "[09:05:03] fits", "[09:05:03] Warning: The log reached its 10 MB limit for today; logging resumes tomorrow." },
            tail.Select(line => line.TrimStart('\0')).ToArray());
        Assert.Equal(new[] { "[09:05:03] a new day" }, ReadLines(LogFile("20261019")));
    }

    [Fact]
    public void Reopening_on_a_day_already_over_the_cap_writes_nothing()
    {
        Directory.CreateDirectory(_folder);
        using (var existing = File.Create(LogFile("20261018"))) existing.SetLength(1000);

        using (var log = NewLog(maxFileBytes: 500)) log.Info("dropped");

        Assert.Equal(1000, new FileInfo(LogFile("20261018")).Length);
    }

    [Fact]
    public void Another_reader_can_read_the_live_file()
    {
        using var log = NewLog();
        log.Info("first");

        Assert.Equal(new[] { "[09:05:03] first" }, ReadLines(LogFile("20261018")));

        log.Info("second");
        Assert.Equal(new[] { "[09:05:03] first", "[09:05:03] second" }, ReadLines(LogFile("20261018")));
    }

    [Fact]
    public void A_folder_that_cannot_be_created_drops_the_line_and_the_next_write_tries_again()
    {
        File.WriteAllText(_folder, "a file where the folder should be");
        using var log = NewLog();

        log.Info("dropped");
        log.Warn("dropped too");

        File.Delete(_folder);
        log.Info("written");

        Assert.Equal(new[] { "[09:05:03] written" }, ReadLines(LogFile("20261018")));
    }

    [Fact]
    public void Writes_after_dispose_are_dropped()
    {
        var log = NewLog();
        log.Info("before");
        log.Dispose();

        log.Info("after");
        log.Warn("after");
        log.Error("after");
        log.Dispose();

        Assert.Equal(new[] { "[09:05:03] before" }, ReadLines(LogFile("20261018")));
    }

    [Fact]
    public void Lines_from_many_threads_stay_whole()
    {
        using (var log = NewLog())
        {
            Parallel.For(0, 8, thread =>
            {
                for (var i = 0; i < 200; i++) log.Info($"thread {thread} line {i}");
            });
        }

        var lines = ReadLines(LogFile("20261018"));
        Assert.Equal(1600, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^\[09:05:03\] thread \d line \d+$", line));
    }
}
