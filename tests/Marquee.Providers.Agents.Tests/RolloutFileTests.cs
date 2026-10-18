using System.Diagnostics;
using System.Text;
using Marquee.Providers.Agents.Codex;
using Marquee.Providers.Agents.Tests.TestSupport;

namespace Marquee.Providers.Agents.Tests;

public class RolloutFileTests : IDisposable
{
    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    string Write(string contents) => _dir.Write("rollout-test.jsonl", contents);

    [Fact]
    public void Lines_come_back_newest_first()
    {
        var path = Write("one\ntwo\nthree\n");

        Assert.Equal(["three", "two", "one"], RolloutFile.LinesFromEnd(path, 1000));
    }

    [Fact]
    public void A_last_line_without_a_newline_is_returned()
    {
        var path = Write("one\ntwo");

        Assert.Equal(["two", "one"], RolloutFile.LinesFromEnd(path, 1000));
    }

    [Fact]
    public void Crlf_and_empty_lines_are_handled()
    {
        var path = Write("one\r\n\r\ntwo\r\n");

        Assert.Equal(["two", "one"], RolloutFile.LinesFromEnd(path, 1000));
    }

    [Fact]
    public void The_line_cut_by_the_limit_is_skipped()
    {
        var path = Write("aaaaaaaaaa\nbbb\nccc\n"); // the last 10 bytes are "a\nbbb\nccc\n": the window starts mid-line, so that "a" is cut

        Assert.Equal(["ccc", "bbb"], RolloutFile.LinesFromEnd(path, 10));
    }

    [Fact]
    public void A_window_with_no_newline_yields_nothing()
    {
        var path = Write("one\n" + new string('a', 50) + "\n"); // the last 10 bytes are all of one long line's tail

        Assert.Empty(RolloutFile.LinesFromEnd(path, 10));
    }

    [Fact]
    public void A_crlf_split_across_a_chunk_boundary_still_loses_its_cr()
    {
        // Chunks are read from the end of the file in 64 KB steps, so the first chunk starts at the '\n' (index 6)
        // and the next one ends with the '\r'.
        var tail = new string('x', 64 * 1024 - 2);
        var path = Write($"alpha\r\n{tail}\n");
        Assert.Equal(64 * 1024 + 6, new FileInfo(path).Length);

        Assert.Equal([tail, "alpha"], RolloutFile.LinesFromEnd(path, 10_000_000));
    }

    [Fact]
    public void A_multi_megabyte_line_is_read_in_linear_time()
    {
        var huge = new string('y', 3_000_000);
        var path = Write($"{huge}\ntail\n");

        var watch = Stopwatch.StartNew();
        var lines = RolloutFile.LinesFromEnd(path, 8 * 1024 * 1024).ToList();
        watch.Stop();

        Assert.Equal(["tail", huge], lines);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Fact]
    public void Lines_longer_than_a_chunk_and_multibyte_text_survive_chunk_boundaries()
    {
        var long1 = string.Concat(Enumerable.Repeat("é—€", 40_000)); // 8 bytes each (2+3+3): 320 KB, several chunks
        var long2 = new string('x', 70_000);
        var path = Write($"{long1}\nshort\n{long2}\n");

        Assert.Equal([long2, "short", long1], RolloutFile.LinesFromEnd(path, 10_000_000));
    }

    [Fact]
    public void Reading_stops_early_when_the_caller_stops()
    {
        var path = Write(string.Join("\n", Enumerable.Range(0, 100_000).Select(i => $"line {i}")) + "\n");

        Assert.Equal("line 99999", RolloutFile.LinesFromEnd(path, long.MaxValue).First());
    }

    [Fact]
    public void A_file_being_appended_to_can_be_read()
    {
        var path = Write("one\n");
        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer.Write(Encoding.UTF8.GetBytes("two\n"));
        writer.Flush();

        Assert.Equal(["two", "one"], RolloutFile.LinesFromEnd(path, 1000));
    }

    [Fact]
    public void First_line_is_read_up_to_a_limit()
    {
        var path = Write("{\"type\":\"session_meta\"}\nrest\n");

        Assert.Equal("{\"type\":\"session_meta\"}", RolloutFile.FirstLine(path, 1000));
        Assert.Null(RolloutFile.FirstLine(path, 10));
    }

    [Fact]
    public void Newest_first_orders_by_write_time_across_folders()
    {
        var old = _dir.Write("2026/10/06/rollout-a.jsonl", "a\n");
        var recent = _dir.Write("2026/10/17/rollout-b.jsonl", "b\n");
        var middle = _dir.Write("2026/10/17/rollout-c.jsonl", "c\n");
        _dir.Write("2026/10/17/notes.txt", "not a rollout");
        File.SetLastWriteTimeUtc(old, new DateTime(2026, 10, 17, 12, 0, 0)); // an old thread still being written
        File.SetLastWriteTimeUtc(recent, new DateTime(2026, 10, 17, 11, 0, 0));
        File.SetLastWriteTimeUtc(middle, new DateTime(2026, 10, 17, 10, 0, 0));

        Assert.Equal(["rollout-a.jsonl", "rollout-b.jsonl", "rollout-c.jsonl"], RolloutFile.NewestFirst(_dir.Path).Select(f => f.Name));
    }

    [Fact]
    public void Newest_first_of_a_missing_folder_is_empty() => Assert.Empty(RolloutFile.NewestFirst(_dir.File("nope")));
}
