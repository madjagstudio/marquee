using Marquee.Providers.Agents;
using Marquee.Providers.Agents.Claude;

/// `marquee setup-claude [--undo] [--dry-run]`: routes Claude Code's status line through `marquee statusline`, or back.
static class SetupClaudeCommand
{
    /// Null means the options were wrong and the caller should print usage.
    public static int? Run(string[] options)
    {
        var undo = false;
        var dryRun = false;
        foreach (var option in options)
        {
            switch (option)
            {
                case "--undo": undo = true; break;
                case "--dry-run": dryRun = true; break;
                default: return null;
            }
        }

        var exe = Environment.ProcessPath;
        if (!undo && (exe is null || !Path.GetFileNameWithoutExtension(exe).Equals("marquee", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"Run setup-claude from marquee.exe itself (this is {exe ?? "an unknown program"}), so Claude Code gets a command that works on its own.");
            return 1;
        }

        try
        {
            var gitBash = ClaudeShell.FindGitBash(Environment.GetEnvironmentVariable, File.Exists) is not null;
            var change = undo
                ? ClaudeSettingsEditor.Undo(AgentPaths.ClaudeSettings, AgentPaths.SavedStatusLine, DateTimeOffset.Now, dryRun)
                : ClaudeSettingsEditor.Install(AgentPaths.ClaudeSettings, AgentPaths.SavedStatusLine,
                    ClaudeSettingsEditor.BridgeCommand(exe!, gitBash), DateTimeOffset.Now, dryRun);

            if (dryRun && change.NewSettings is { } text)
            {
                Console.WriteLine($"Dry run: nothing was written. {AgentPaths.ClaudeSettings} would become:");
                Console.WriteLine(text);
            }

            // A dry run's message says what the real run would do, in the words the real run uses.
            Console.WriteLine(dryRun && change.Changed ? $"Without --dry-run: {change.Message}" : change.Message);
            if (change.Changed && !undo && !dryRun)
                Console.WriteLine("Limits appear once `marquee run` is going and a Claude Code session updates its status line. " +
                    "A project's .claude/settings.json or managed settings with their own statusLine take precedence over this one.");
            return 0;
        }
        catch (ClaudeSetupException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not update Claude Code's settings: {ex.Message}");
            return 1;
        }
    }
}
