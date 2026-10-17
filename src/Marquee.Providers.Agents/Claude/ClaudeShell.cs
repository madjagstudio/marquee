namespace Marquee.Providers.Agents.Claude;

/// The shell Claude Code runs status-line commands in, so `marquee statusline` can run the user's previous command
/// the same way. Claude Code's docs: "On Windows, Claude Code runs status line commands through Git Bash when Git Bash
/// is installed, or through PowerShell when Git Bash is absent." CLAUDE_CODE_GIT_BASH_PATH points it at a bash.exe.
public static class ClaudeShell
{
    public static ShellCommand Resolve() => Resolve(Environment.GetEnvironmentVariable, File.Exists);

    public static ShellCommand Resolve(Func<string, string?> environment, Func<string, bool> fileExists) =>
        FindGitBash(environment, fileExists) is { } bash ? ShellCommand.Bash(bash) : ShellCommand.PowerShell;

    /// Git Bash's bash.exe: CLAUDE_CODE_GIT_BASH_PATH, else next to a git.exe on PATH, else the default install folder.
    public static string? FindGitBash(Func<string, string?> environment, Func<string, bool> fileExists)
    {
        if (environment("CLAUDE_CODE_GIT_BASH_PATH") is { Length: > 0 } configured && fileExists(configured))
            return configured;

        foreach (var directory in (environment("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!fileExists(Path.Combine(directory, "git.exe"))) continue;
            // git.exe lives in <Git>\cmd, <Git>\bin or <Git>\mingw64\bin; bash.exe in <Git>\bin.
            foreach (var root in new[] { Path.Combine(directory, ".."), Path.Combine(directory, "..", "..") })
            {
                var bash = Path.GetFullPath(Path.Combine(root, "bin", "bash.exe"));
                if (fileExists(bash)) return bash;
            }
        }

        foreach (var programFiles in new[] { environment("ProgramFiles"), environment("ProgramW6432") })
        {
            if (programFiles is not { Length: > 0 }) continue;
            var bash = Path.Combine(programFiles, "Git", "bin", "bash.exe");
            if (fileExists(bash)) return bash;
        }

        return null;
    }
}
