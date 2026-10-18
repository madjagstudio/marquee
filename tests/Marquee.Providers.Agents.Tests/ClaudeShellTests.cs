using Marquee.Providers.Agents.Claude;

namespace Marquee.Providers.Agents.Tests;

public class ClaudeShellTests
{
    static ShellCommand Resolve(Dictionary<string, string> environment, params string[] files) =>
        ClaudeShell.Resolve(name => environment.GetValueOrDefault(name), path => files.Contains(path, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Git_bash_path_setting_wins()
    {
        var shell = Resolve(new() { ["CLAUDE_CODE_GIT_BASH_PATH"] = @"D:\tools\bash.exe", ["PATH"] = @"C:\Program Files\Git\cmd" },
            @"D:\tools\bash.exe", @"C:\Program Files\Git\cmd\git.exe", @"C:\Program Files\Git\bin\bash.exe");

        Assert.Equal(@"D:\tools\bash.exe", shell.FileName);
        Assert.Equal(["-c"], shell.Arguments);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Git\cmd")]
    [InlineData(@"C:\Program Files\Git\bin")]
    [InlineData(@"C:\Program Files\Git\mingw64\bin")]
    public void Bash_is_found_from_git_on_the_path(string gitFolder)
    {
        var shell = Resolve(new() { ["PATH"] = $@"C:\Windows;{gitFolder}" }, $@"{gitFolder}\git.exe", @"C:\Program Files\Git\bin\bash.exe");

        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", shell.FileName);
        Assert.Equal(["-c"], shell.Arguments);
    }

    [Fact]
    public void Bash_is_found_in_the_default_install_folder()
    {
        var shell = Resolve(new() { ["PATH"] = @"C:\Windows", ["ProgramFiles"] = @"C:\Program Files" }, @"C:\Program Files\Git\bin\bash.exe");

        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", shell.FileName);
    }

    [Fact]
    public void Bash_is_found_in_the_32_bit_program_files_folder()
    {
        var shell = Resolve(new() { ["PATH"] = @"C:\Windows", ["ProgramFiles(x86)"] = @"C:\Program Files (x86)" },
            @"C:\Program Files (x86)\Git\bin\bash.exe");

        Assert.Equal(@"C:\Program Files (x86)\Git\bin\bash.exe", shell.FileName);
    }

    [Fact]
    public void A_missing_configured_bash_falls_back_to_the_search()
    {
        var shell = Resolve(new() { ["CLAUDE_CODE_GIT_BASH_PATH"] = @"D:\gone\bash.exe", ["ProgramFiles"] = @"C:\Program Files" },
            @"C:\Program Files\Git\bin\bash.exe");

        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", shell.FileName);
    }

    [Fact]
    public void Without_git_bash_it_is_powershell()
    {
        var shell = Resolve(new() { ["PATH"] = @"C:\Windows", ["ProgramFiles"] = @"C:\Program Files" });

        Assert.Equal(ShellCommand.PowerShell, shell);
        Assert.Equal(["-NoProfile", "-Command"], shell.Arguments);
    }
}
