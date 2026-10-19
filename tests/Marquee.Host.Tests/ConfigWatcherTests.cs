using System.Collections.Concurrent;
using Marquee.Core.Config;
using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

public class ConfigWatcherTests
{
    /// How long a test waits to be sure something did not happen: several debounces.
    static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1.5);

    /// Records callbacks from the watcher's thread so tests can wait for them.
    sealed class Events
    {
        public BlockingCollection<MarqueeConfig> Changed { get; } = [];
        public BlockingCollection<ConfigException> Failed { get; } = [];

        public ConfigWatcher Watch(string path) => new(path, Changed.Add, Failed.Add);

        public MarqueeConfig NextChange()
        {
            Assert.True(Changed.TryTake(out var config, Wait.Timeout), "No change was reported.");
            return config!;
        }
    }

    [Fact]
    public void A_burst_of_saves_is_reported_once_with_the_last_contents()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.yaml", "fps: 10\n");
        var events = new Events();
        using var watcher = events.Watch(path);
        Assert.True(watcher.Start());

        File.WriteAllText(path, "fps: 20\n");
        File.WriteAllText(path, "fps: 30\n");
        File.WriteAllText(path, "fps: 40\n");

        Assert.Equal(40, events.NextChange().Fps);
        Assert.False(events.Changed.TryTake(out _, Quiet), "The burst was reported more than once.");
        Assert.Empty(events.Failed);
    }

    [Fact]
    public void A_broken_save_reports_the_error_with_its_line_and_the_fix_reports_the_change()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.yaml", "fps: 10\n");
        var events = new Events();
        using var watcher = events.Watch(path);
        watcher.Start();

        File.WriteAllText(path, "theme: neon\nfps: 0\n");
        Assert.True(events.Failed.TryTake(out var error, Wait.Timeout), "No error after a broken save.");
        Assert.Equal("config.yaml line 2: fps must be between 1 and 60", error!.Message);

        File.WriteAllText(path, "theme: neon\nfps: 15\n");
        Assert.Equal(15, events.NextChange().Fps);
    }

    [Fact]
    public void A_save_by_renaming_a_temporary_file_over_it_is_seen()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.yaml", "fps: 10\n");
        var events = new Events();
        using var watcher = events.Watch(path);
        watcher.Start();

        var temporary = dir.Write("config.yaml.tmp", "fps: 25\n");
        File.Move(temporary, path, overwrite: true);

        Assert.Equal(25, events.NextChange().Fps);
    }

    [Fact]
    public void A_file_created_after_the_watch_began_is_seen()
    {
        using var dir = new TempDir();
        var path = dir.File("config.yaml");
        var events = new Events();
        using var watcher = events.Watch(path);
        watcher.Start();

        File.WriteAllText(path, "theme: neon\n");

        Assert.Equal("neon", events.NextChange().Theme);
    }

    [Fact]
    public void Deleting_the_file_goes_back_to_the_defaults()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.yaml", "fps: 10\n");
        var events = new Events();
        using var watcher = events.Watch(path);
        watcher.Start();

        File.Delete(path);

        Assert.Equal(MarqueeConfig.Default, events.NextChange());
    }

    [Fact]
    public void Other_files_in_the_folder_are_ignored()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.yaml", "fps: 10\n");
        var events = new Events();
        using var watcher = events.Watch(path);
        watcher.Start();

        dir.Write("claude-limits.json", "{}\n");
        dir.Write(@"logs\marquee-20261018.log", "[12:00:00] hello\n");

        Assert.False(events.Changed.TryTake(out _, Quiet), "A change to another file was reported.");
        Assert.Empty(events.Failed);
    }

    [Fact]
    public void A_folder_that_does_not_exist_is_not_watched_or_created()
    {
        using var dir = new TempDir();
        var folder = dir.File("Marquee");
        var events = new Events();
        using var watcher = events.Watch(Path.Combine(folder, "config.yaml"));

        Assert.False(watcher.Start());
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Nothing_is_reported_after_dispose()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.yaml", "fps: 10\n");
        var events = new Events();
        var watcher = events.Watch(path);
        watcher.Start();

        watcher.Dispose();
        File.WriteAllText(path, "fps: 20\n");

        Assert.False(events.Changed.TryTake(out _, Quiet), "A change was reported after dispose.");
    }
}
