using Marquee.Providers.Agents;
using Marquee.Providers.Agents.Claude;

/// `marquee statusline`: Claude Code's status-line command once `marquee setup-claude` has run. See StatusLineBridge.
static class StatusLineCommand
{
    public static int Run()
    {
        SavedStatusLine.TryLoad(AgentPaths.SavedStatusLine, out var saved);
        var previous = SavedStatusLine.Command(saved);
        var shell = previous is null ? ShellCommand.PowerShell : ClaudeShell.Resolve();

        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        return StatusLineBridge.Run(stdin, stdout, Console.Error, previous, shell,
            json => StatusLinePipe.TrySend(json, StatusLineBridge.ForwardTimeout));
    }
}
