using System.Collections.Concurrent;
using System.Globalization;
using Marquee.App.Tests.TestSupport;
using Marquee.Host.Updates;

namespace Marquee.App.Tests;

/// The update schedule, checks and downloads, over a fake GitHub, with a fake clock and a fake delay so nothing waits a
/// day (or five minutes).
public sealed class TrayUpdatesTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly UpdateRig _rig;
    readonly ConcurrentQueue<UpdateOffer> _offered = new();

    public TrayUpdatesTests()
    {
        _rig = new UpdateRig(_dir);
        Updates.Offered += _offered.Enqueue;
    }

    TrayUpdates Updates => _rig.Updates;

    public void Dispose()
    {
        _rig.Dispose();
        _dir.Dispose();
    }

    DateTimeOffset? Recorded() =>
        File.Exists(_rig.StateFile)
            ? DateTimeOffset.ParseExact(File.ReadAllText(_rig.StateFile).Trim(), "O", CultureInfo.InvariantCulture)
            : null;

    [Fact]
    public void The_first_check_waits_until_a_few_minutes_after_start_then_checks_and_waits_a_day()
    {
        Updates.SetEnabled(true);

        Assert.True(_rig.Delay.WaitForRequests(1));
        Assert.Equal(UpdateSchedule.FirstCheckAfterStart, _rig.Delay.Requested[0]);
        Assert.Equal(0, _rig.GitHub.ApiRequests);

        _rig.Now += UpdateSchedule.FirstCheckAfterStart;
        _rig.Delay.Elapse();

        Assert.True(_rig.Delay.WaitForRequests(2));
        Assert.Equal(1, _rig.GitHub.ApiRequests);
        Assert.Equal(_rig.Now, Recorded());
        Assert.Equal(UpdateSchedule.Interval, _rig.Delay.Requested[1]);
    }

    [Fact]
    public void A_check_made_earlier_today_puts_the_next_one_off_until_a_day_after_it()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_rig.StateFile)!);
        File.WriteAllText(_rig.StateFile, (_rig.Now - TimeSpan.FromHours(2)).ToString("O", CultureInfo.InvariantCulture));

        Updates.SetEnabled(true);

        Assert.True(_rig.Delay.WaitForRequests(1));
        Assert.Equal(TimeSpan.FromHours(22), _rig.Delay.Requested[0]);
    }

    [Fact]
    public async Task With_updates_off_nothing_checks_on_its_own_but_a_manual_check_does()
    {
        Updates.SetEnabled(false);

        Assert.True(_rig.Delay.WaitForRequests(1));
        Assert.Equal(Timeout.InfiniteTimeSpan, _rig.Delay.Requested[0]);

        var result = await Updates.CheckNowAsync().WaitAsync(Wait.Timeout);

        Assert.Equal(new Version(0, 2, 0), result.Offer?.Version);
        Assert.Equal(new Version(0, 2, 0), Updates.Offer?.Version);
        Assert.Equal(1, _rig.GitHub.ApiRequests);
        Assert.Equal(_rig.Now, Recorded());
        Assert.Empty(_offered); // the user asked, so whoever asked shows it
    }

    [Fact]
    public void Turning_updates_on_in_config_yaml_starts_the_schedule_and_off_stops_it()
    {
        Updates.SetEnabled(false);
        Assert.True(_rig.Delay.WaitForRequests(1));

        Updates.SetEnabled(true);
        Assert.True(_rig.Delay.WaitForRequests(2));
        Assert.Equal(UpdateSchedule.FirstCheckAfterStart, _rig.Delay.Requested[1]);

        Updates.SetEnabled(true); // a config.yaml edit that changes something else
        Updates.SetEnabled(false);
        Assert.True(_rig.Delay.WaitForRequests(3));
        Assert.Equal(Timeout.InfiniteTimeSpan, _rig.Delay.Requested[2]);
        Assert.Equal(3, _rig.Delay.Requested.Count);
        Assert.Equal(0, _rig.GitHub.ApiRequests);
    }

    [Fact]
    public void A_check_that_fails_is_recorded_so_the_next_is_a_day_later_not_at_once()
    {
        _rig.GitHub.ApiFailure = new HttpRequestException("No such host is known.");
        Updates.SetEnabled(true);
        Assert.True(_rig.Delay.WaitForRequests(1));

        _rig.Now += UpdateSchedule.FirstCheckAfterStart;
        _rig.Delay.Elapse();

        Assert.True(_rig.Delay.WaitForRequests(2));
        Assert.Equal(1, _rig.GitHub.ApiRequests);
        Assert.Equal(_rig.Now, Recorded());
        Assert.Equal(UpdateSchedule.Interval, _rig.Delay.Requested[1]);
        Assert.Null(Updates.Offer);
        Assert.Empty(_offered);
    }

    [Fact]
    public void A_check_that_throws_unexpectedly_is_recorded_too_so_the_timer_never_checks_in_a_loop()
    {
        // The checker returns its failures rather than throwing; this stands in for a bug.
        _rig.GitHub.ApiFailure = new InvalidOperationException("a bug");
        Updates.SetEnabled(true);
        Assert.True(_rig.Delay.WaitForRequests(1));

        _rig.Now += UpdateSchedule.FirstCheckAfterStart;
        _rig.Delay.Elapse();

        Assert.True(_rig.Delay.WaitForRequests(2));
        Assert.Equal(1, _rig.GitHub.ApiRequests);
        Assert.Equal(_rig.Now, Recorded());
        Assert.Equal(UpdateSchedule.Interval, _rig.Delay.Requested[1]);
    }

    [Fact]
    public void A_newer_release_found_on_schedule_is_offered_once_however_often_it_is_found()
    {
        Updates.SetEnabled(true);
        Assert.True(_rig.Delay.WaitForRequests(1));

        _rig.Delay.Elapse();
        Assert.True(_rig.Delay.WaitForRequests(2));
        _rig.Now += UpdateSchedule.Interval;
        _rig.Delay.Elapse();
        Assert.True(_rig.Delay.WaitForRequests(3));

        Assert.Equal(2, _rig.GitHub.ApiRequests);
        Assert.Equal(new Version(0, 2, 0), Assert.Single(_offered).Version);
        Assert.Equal(new Version(0, 2, 0), Updates.Offer?.Version);
    }

    [Fact]
    public async Task A_manual_check_puts_the_daily_one_off_for_a_day()
    {
        Updates.SetEnabled(true);
        Assert.True(_rig.Delay.WaitForRequests(1));

        _rig.Now += TimeSpan.FromMinutes(1);
        await Updates.CheckNowAsync().WaitAsync(Wait.Timeout);

        Assert.True(_rig.Delay.WaitForRequests(2));
        Assert.Equal(UpdateSchedule.Interval, _rig.Delay.Requested[1]);
    }

    [Fact]
    public void With_the_placeholder_key_nothing_checks_on_its_own()
    {
        using var dir = new TempDir();
        using var rig = new UpdateRig(dir, placeholderKey: true);

        rig.Updates.SetEnabled(true);

        Assert.False(rig.Updates.HasReleaseKey);
        Assert.True(rig.Delay.WaitForRequests(1));
        Assert.Equal(Timeout.InfiniteTimeSpan, rig.Delay.Requested[0]);
    }

    [Fact]
    public async Task A_download_makes_the_folder_admin_only_first_reports_progress_and_verifies()
    {
        var offer = (await Updates.CheckNowAsync()).Offer!;
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _rig.GitHub.HoldInstallerHalfway = hold;

        var download = Updates.DownloadAsync(offer);

        Assert.True(await Wait.UntilAsync(() => Updates.Downloading?.Percent == 50));
        Assert.Same(offer, Updates.Downloading!.Offer);
        Assert.Equal([_rig.Downloads], _rig.SecuredFolders);
        Assert.Null(await Updates.DownloadAsync(offer)); // one at a time

        hold.SetResult();
        using var update = await download.WaitAsync(Wait.Timeout);

        Assert.NotNull(update);
        Assert.Equal(Path.Combine(_rig.Downloads, "Marquee-0.2.0-x64.msi"), update.InstallerPath);
        Assert.Null(Updates.Downloading);
    }

    [Fact]
    public async Task A_tampered_installer_is_refused()
    {
        var offer = (await Updates.CheckNowAsync()).Offer!;
        _rig.GitHub.TamperedInstaller = true;

        await Assert.ThrowsAsync<UpdateException>(() => Updates.DownloadAsync(offer).WaitAsync(Wait.Timeout));

        Assert.Null(Updates.Downloading);
        Assert.Empty(Directory.GetFiles(_rig.Downloads, "*.msi"));
    }

    [Fact]
    public async Task Dispose_cancels_a_download_under_way()
    {
        var offer = (await Updates.CheckNowAsync()).Offer!;
        _rig.GitHub.HoldInstallerHalfway = new TaskCompletionSource();
        var download = Updates.DownloadAsync(offer);
        Assert.True(await Wait.UntilAsync(() => Updates.Downloading?.Percent == 50));

        Updates.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download.WaitAsync(Wait.Timeout));
        Assert.Empty(Directory.GetFiles(_rig.Downloads, "*.msi*"));
    }
}
