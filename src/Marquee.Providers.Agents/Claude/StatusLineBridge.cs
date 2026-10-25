namespace Marquee.Providers.Agents.Claude;

/// `marquee statusline`, which Claude Code runs as its status-line command: hand the JSON on stdin to the running app,
/// then run the user's previous status-line command with the same input and print what it prints, so the user's
/// status line looks exactly as it did before. Marquee not running is normal and changes nothing.
public static class StatusLineBridge
{
    /// Claude Code sends a few kilobytes; anything past this is read and dropped.
    public const int MaxInputBytes = 1024 * 1024;

    /// How long to wait for Marquee to take the message. The previous command runs meanwhile.
    public static readonly TimeSpan ForwardTimeout = TimeSpan.FromMilliseconds(100);

    /// The previous command is stopped after this long.
    public static readonly TimeSpan PreviousTimeout = TimeSpan.FromSeconds(10);

    /// Returns the exit code: the previous command's, or 0 when there is none (the status line stays blank, as it was).
    /// Returns 1 when the shell can't start or the command times out.
    public static int Run(Stream stdin, Stream stdout, TextWriter stderr, string? previousCommand, ShellCommand shell,
        Func<byte[], bool> forward, TimeSpan? previousTimeout = null)
    {
        var timeout = previousTimeout ?? PreviousTimeout;
        var input = ReadCapped(stdin);
        var previous = previousCommand is null ? null : Task.Run(() => shell.Run(previousCommand, input, timeout));

        try
        {
            forward(input);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"marquee statusline: could not reach Marquee: {ex.Message}");
        }

        if (previous is null) return 0;

        ShellResult result;
        try
        {
            result = previous.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"marquee statusline: could not run the previous status-line command with {shell.FileName}: {ex.Message}");
            return 1;
        }

        stdout.Write(result.Output);
        stdout.Flush();
        if (!result.TimedOut) return result.ExitCode;

        stderr.WriteLine($"marquee statusline: the previous status-line command took longer than {timeout.TotalSeconds:0} s and was stopped.");
        return 1;
    }

    static byte[] ReadCapped(Stream stdin)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = stdin.Read(chunk)) > 0)
        {
            var keep = (int)Math.Min(read, MaxInputBytes - buffer.Length);
            if (keep > 0) buffer.Write(chunk, 0, keep);
        }

        return buffer.ToArray();
    }
}
