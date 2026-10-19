using Marquee.Host.Tests.TestSupport;
using Marquee.Host.Updates;

namespace Marquee.Host.Tests;

public sealed class UpdateScheduleTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 10, 19, 8, 0, 0, TimeSpan.FromHours(2));

    readonly TempDir temp = new();
    readonly ListLog log = new();

    public void Dispose() => temp.Dispose();

    string StateFile => temp.File(UpdateSchedule.FileName);

    UpdateSchedule Schedule(DateTimeOffset startedAt, bool enabled = true) => new(StateFile, enabled, startedAt, log);

    [Fact]
    public void The_first_check_is_five_minutes_after_start()
    {
        var schedule = Schedule(Start);

        Assert.Equal(TimeSpan.FromMinutes(5), UpdateSchedule.FirstCheckAfterStart);
        Assert.Equal(TimeSpan.FromMinutes(5), schedule.NextCheckIn(Start));
        Assert.Equal(TimeSpan.FromMinutes(3), schedule.NextCheckIn(Start.AddMinutes(2)));
        Assert.Equal(TimeSpan.Zero, schedule.NextCheckIn(Start.AddMinutes(5)));
        Assert.Equal(TimeSpan.Zero, schedule.NextCheckIn(Start.AddHours(1)));
    }

    [Fact]
    public void After_a_check_the_next_is_a_day_later()
    {
        var schedule = Schedule(Start);
        var checkedAt = Start.AddMinutes(5);

        schedule.RecordCheck(checkedAt);

        Assert.Equal(TimeSpan.FromDays(1), schedule.NextCheckIn(checkedAt));
        Assert.Equal(TimeSpan.FromHours(20), schedule.NextCheckIn(checkedAt.AddHours(4)));
        Assert.Equal(TimeSpan.Zero, schedule.NextCheckIn(checkedAt.AddDays(1)));
    }

    [Fact]
    public void A_restart_remembers_the_last_check()
    {
        var checkedAt = Start.AddMinutes(5);
        Schedule(Start).RecordCheck(checkedAt);

        var restartedAt = checkedAt.AddHours(2);
        var afterRestart = Schedule(restartedAt);

        Assert.Equal(checkedAt, afterRestart.LastCheck);
        Assert.Equal(TimeSpan.FromHours(22), afterRestart.NextCheckIn(restartedAt));
    }

    [Fact]
    public void A_restart_after_more_than_a_day_still_waits_the_first_few_minutes()
    {
        Schedule(Start).RecordCheck(Start.AddMinutes(5));

        var restartedAt = Start.AddDays(3);

        Assert.Equal(TimeSpan.FromMinutes(5), Schedule(restartedAt).NextCheckIn(restartedAt));
    }

    [Fact]
    public void A_restart_just_before_the_day_is_up_waits_the_first_few_minutes_too()
    {
        var checkedAt = Start.AddMinutes(5);
        Schedule(Start).RecordCheck(checkedAt);

        var restartedAt = checkedAt.AddDays(1).AddMinutes(-1);

        Assert.Equal(TimeSpan.FromMinutes(5), Schedule(restartedAt).NextCheckIn(restartedAt));
    }

    [Fact]
    public void The_last_check_is_kept_as_one_line_in_UTC()
    {
        Schedule(Start).RecordCheck(Start);

        Assert.Equal("2026-10-19T06:00:00.0000000+00:00\n", File.ReadAllText(StateFile));
    }

    [Fact]
    public void Updates_off_never_checks()
    {
        var schedule = Schedule(Start, enabled: false);

        Assert.Equal(Timeout.InfiniteTimeSpan, schedule.NextCheckIn(Start));
        Assert.Equal(Timeout.InfiniteTimeSpan, schedule.NextCheckIn(Start.AddDays(30)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("2026-13-45T99:00:00Z")]
    public void An_unreadable_file_counts_as_never_checked(string contents)
    {
        File.WriteAllText(StateFile, contents);

        var schedule = Schedule(Start);

        Assert.Null(schedule.LastCheck);
        Assert.Equal(TimeSpan.FromMinutes(5), schedule.NextCheckIn(Start));
    }

    [Fact]
    public void A_last_check_in_the_future_is_ignored_so_a_wrong_clock_cant_stop_checks()
    {
        Schedule(Start).RecordCheck(Start.AddYears(1));

        Assert.Equal(TimeSpan.FromMinutes(5), Schedule(Start).NextCheckIn(Start));
    }

    [Fact]
    public void A_file_that_cant_be_written_is_logged_and_the_check_still_counts_until_restart()
    {
        Directory.CreateDirectory(StateFile); // a folder where the file should be
        var schedule = Schedule(Start);

        schedule.RecordCheck(Start);

        Assert.Equal(TimeSpan.FromDays(1), schedule.NextCheckIn(Start));
        Assert.StartsWith("warn: ", Assert.Single(log.Entries));
    }
}
