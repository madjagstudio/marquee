using System.Globalization;
using Marquee.Core.Files;
using Marquee.Core.Logging;
using Marquee.Host.Resources;

namespace Marquee.Host.Updates;

/// When the tray next looks for an update: once a day, and never sooner than a few minutes after the tray starts, so
/// the check stays out of the way of logon, when the PC is busiest. The last check is kept in a small file (FileName,
/// in the tray's data folder: MarqueePaths.ProcessDataFolder), so a PC that restarts often still checks once a day, not
/// at every boot. The tray records every check it makes, whether it worked or not, so a GitHub it can't reach is asked
/// again the next day rather than every few minutes. `updates: off` (`enabled` false) means never.
///
/// The tray owns the timer: it waits NextCheckIn, checks, calls RecordCheck, and asks NextCheckIn again. Thread-safe.
public sealed class UpdateSchedule
{
    public const string FileName = "last-update-check.txt";

    public static readonly TimeSpan FirstCheckAfterStart = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    readonly object gate = new();
    readonly string stateFile;
    readonly bool enabled;
    readonly DateTimeOffset startedAt;
    readonly ILog log;
    DateTimeOffset? lastCheck;

    public UpdateSchedule(string stateFile, bool enabled, DateTimeOffset startedAt, ILog log)
    {
        this.stateFile = stateFile;
        this.enabled = enabled;
        this.startedAt = startedAt;
        this.log = log;
        lastCheck = Load(stateFile);
    }

    /// When the last check was, as RecordCheck saved it, or null when there is none (or the file can't be read).
    public DateTimeOffset? LastCheck
    {
        get
        {
            lock (gate) return lastCheck;
        }
    }

    /// How long until the next check is due: zero when it is due now, and Timeout.InfiniteTimeSpan when updates are off,
    /// which System.Threading.Timer and Task.Delay both take as never.
    public TimeSpan NextCheckIn(DateTimeOffset now)
    {
        if (!enabled) return Timeout.InfiniteTimeSpan;

        var due = startedAt + FirstCheckAfterStart;
        lock (gate)
        {
            // A last check in the future means the clock was wrong then or is now. Trusting it could put the next check
            // off for as long, so it is ignored, and the check made a few minutes from now records the right time.
            if (lastCheck is { } last && last <= now && last + Interval > due) due = last + Interval;
        }

        return due > now ? due - now : TimeSpan.Zero;
    }

    /// Notes that a check was made at `now`, here and in the file. A file that can't be written is logged; the check
    /// still counts until the tray restarts.
    public void RecordCheck(DateTimeOffset now)
    {
        lock (gate) lastCheck = now;

        try
        {
            // SafeFile, because the tray runs elevated: it refuses a path with a link on it and replaces the file whole.
            SafeFile.WriteAllText(stateFile, now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn(Strings.UpdateCheckTimeNotSaved(stateFile, ex.Message));
        }
    }

    /// The time in the file: one line, round-trip format ("O"). Anything else counts as never checked, which costs one
    /// check a few minutes after start.
    static DateTimeOffset? Load(string stateFile)
    {
        string text;
        try
        {
            text = File.ReadAllText(stateFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(text.Trim(), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    }
}
