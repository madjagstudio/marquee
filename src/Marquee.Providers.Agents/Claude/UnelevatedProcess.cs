using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using static Marquee.Providers.Agents.Claude.NativeMethods;

namespace Marquee.Providers.Agents.Claude;

/// Runs a program with the signed-in user's normal (unelevated) token while Marquee runs elevated. Every place
/// `claude.exe` is found is writable by the user's unelevated programs, so starting it elevated would hand them
/// administrator rights. The token is the desktop shell's (Explorer's): GetShellWindow, its process, OpenProcessToken,
/// DuplicateTokenEx, then CreateProcessWithTokenW with the user's own environment block. The child starts suspended, goes
/// into ChildProcessJob.Shared and only then runs, and its stdin, stdout and stderr are anonymous pipes, read the same
/// way as ClaudeCli.RunProcess reads them.
internal static class UnelevatedProcess
{
    const uint ShellTokenAccess = TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY;
    const uint UserTokenAccess = TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID;

    /// Starts `exe` with the desktop shell's token. Throws InvalidOperationException, without starting anything, when
    /// there is no shell (a service or a task that runs before Explorer), the shell is another account's, or any step
    /// fails. Otherwise it behaves like ClaudeCli.RunProcess: TimeoutException at the timeout, with the child
    /// and its children killed, and never a child left running. `exe` must be a full path: it is not searched for.
    public static ProcessResult Run(string exe, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout)
    {
        RequireFullPath(exe);
        KernelHandle token;
        try
        {
            token = ShellUserToken();
        }
        catch (Exception ex) when (ex is Win32Exception or ArgumentException or System.Security.SecurityException)
        {
            throw CannotStart(ex.Message);
        }

        using (token) return Run(token, exe, arguments, workingDirectory, timeout);
    }

    /// The same pipes, job, environment block, suspended start and timeout, but with CreateProcessW under Marquee's own
    /// token. Only tests use it: the shell-token path needs an elevated Marquee.
    internal static ProcessResult RunWithOwnToken(string exe, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout)
    {
        RequireFullPath(exe);
        return Run(null, exe, arguments, workingDirectory, timeout);
    }

    static void RequireFullPath(string exe)
    {
        if (!Path.IsPathFullyQualified(exe)) throw new ArgumentException($"{exe} is not a full path", nameof(exe));
    }

    static InvalidOperationException CannotStart(string detail) =>
        new($"Marquee is running elevated and couldn't start claude without elevation ({detail}); Claude Code limits come only from the status line");

    static Win32Exception Failed(string call)
    {
        var error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, $"{call} failed: {new Win32Exception(error).Message.TrimEnd('.')}");
    }

    /// A primary token copied from the desktop shell's, which must run as Marquee's own Windows account (an elevation
    /// with another account's credentials would otherwise run `claude` as the person signed in to the desktop).
    internal static KernelHandle ShellUserToken()
    {
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero) throw CannotStart("there is no desktop shell, Explorer, to take the unelevated token from");
        if (GetWindowThreadProcessId(shell, out var shellId) == 0 || shellId == 0) throw Failed("GetWindowThreadProcessId");

        using var shellProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, shellId);
        if (shellProcess.IsInvalid) throw Failed("OpenProcess on the shell");
        if (!OpenProcessToken(shellProcess, ShellTokenAccess, out var shellToken)) throw Failed("OpenProcessToken on the shell");

        using (shellToken)
        {
            if (!DuplicateTokenEx(shellToken, UserTokenAccess, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out var token))
                throw Failed("DuplicateTokenEx");
            try
            {
                using var shellUser = new WindowsIdentity(token.DangerousGetHandle());
                using var marqueeUser = WindowsIdentity.GetCurrent();
                if (shellUser.User != marqueeUser.User) throw CannotStart("the desktop shell runs as another Windows account");
                return token;
            }
            catch
            {
                token.Dispose();
                throw;
            }
        }
    }

    static ProcessResult Run(KernelHandle? userToken, string exe, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout)
    {
        using var stdin = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        using var stdout = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var stderr = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

        PROCESS_INFORMATION info;
        try
        {
            info = CreateSuspended(userToken, exe, arguments, workingDirectory, stdin, stdout, stderr);
        }
        catch (Win32Exception ex) when (userToken is not null)
        {
            throw CannotStart(ex.Message);
        }
        finally
        {
            // Only the child holds the other ends now, so the reads below end when it (and anything it started) exits.
            stdin.DisposeLocalCopyOfClientHandle();
            stdout.DisposeLocalCopyOfClientHandle();
            stderr.DisposeLocalCopyOfClientHandle();
        }

        using var process = new KernelHandle(info.hProcess);
        using (var thread = new KernelHandle(info.hThread))
        {
            try
            {
                ChildProcessJob.Shared.Assign(process);
                if (ResumeThread(thread) == ResumeThreadFailed) throw Failed("ResumeThread");
            }
            catch (Win32Exception ex)
            {
                TerminateProcess(process, 1);
                if (userToken is null) throw;
                throw CannotStart(ex.Message);
            }
        }

        stdin.Dispose(); // the child's input ends straight away
        var output = new ClaudeCli.CappedBuffer(ClaudeCli.MaxOutputBytes);
        var reading = Task.Run(() => output.ReadAll(stdout));
        var errors = Task.Run(() => new ClaudeCli.CappedBuffer(0).ReadAll(stderr));
        try
        {
            var waited = WaitForSingleObject(process, Milliseconds(timeout));
            if (waited == WAIT_TIMEOUT)
            {
                Kill(process, info.dwProcessId);
                Task.WaitAll([reading, errors], ClaudeCli.DrainTimeout);
                throw new TimeoutException($"{Path.GetFileName(exe)} did not finish within {timeout.TotalSeconds:0} seconds and was stopped");
            }

            if (waited != WAIT_OBJECT_0) throw Failed("WaitForSingleObject");
            Task.WaitAll([reading, errors], ClaudeCli.DrainTimeout);
            if (!GetExitCodeProcess(process, out var exitCode)) throw Failed("GetExitCodeProcess");
            return new ProcessResult(unchecked((int)exitCode), output.Text());
        }
        finally
        {
            Kill(process, info.dwProcessId); // a no-op once it has exited; the safety net for any other way out
        }
    }

    static PROCESS_INFORMATION CreateSuspended(KernelHandle? userToken, string exe, IReadOnlyList<string> arguments, string workingDirectory,
        AnonymousPipeServerStream stdin, AnonymousPipeServerStream stdout, AnonymousPipeServerStream stderr)
    {
        var environment = userToken is not null ? EnvironmentBlock(userToken) : OwnEnvironmentBlock();
        var startup = new STARTUPINFO
        {
            cb = Marshal.SizeOf<STARTUPINFO>(),
            // CreateProcessWithTokenW always gives a console program a new console; SW_HIDE keeps its window hidden.
            dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW,
            wShowWindow = SW_HIDE,
            hStdInput = stdin.ClientSafePipeHandle.DangerousGetHandle(),
            hStdOutput = stdout.ClientSafePipeHandle.DangerousGetHandle(),
            hStdError = stderr.ClientSafePipeHandle.DangerousGetHandle(),
        };
        const uint flags = CREATE_SUSPENDED | CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT;

        var commandLine = Marshal.StringToHGlobalUni(CommandLine(exe, arguments)); // writable, as both calls require
        var block = Marshal.StringToHGlobalUni(environment);
        try
        {
            PROCESS_INFORMATION info;
            var started = userToken is not null
                ? CreateProcessWithTokenW(userToken, 0, exe, commandLine, flags, block, workingDirectory, ref startup, out info)
                : CreateProcess(exe, commandLine, IntPtr.Zero, IntPtr.Zero, true, flags, block, workingDirectory, ref startup, out info);
            if (!started) throw Failed(userToken is not null ? "CreateProcessWithTokenW" : "CreateProcess");
            return info;
        }
        finally
        {
            Marshal.FreeHGlobal(commandLine);
            Marshal.FreeHGlobal(block);
            GC.KeepAlive(stdin);
            GC.KeepAlive(stdout);
            GC.KeepAlive(stderr);
        }
    }

    static string OwnEnvironmentBlock()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE, out var token)) throw Failed("OpenProcessToken");
        using (token) return EnvironmentBlock(token);
    }

    /// The user's own environment (as at sign-in, not Marquee's), plus ClaudeCli.ChildEnvironment.
    static string EnvironmentBlock(KernelHandle token)
    {
        if (!CreateEnvironmentBlock(out var block, token, false)) throw Failed("CreateEnvironmentBlock");
        try
        {
            return EnvironmentBlock(ReadBlock(block), ClaudeCli.ChildEnvironment);
        }
        finally
        {
            DestroyEnvironmentBlock(block);
        }
    }

    static List<string> ReadBlock(IntPtr block)
    {
        var variables = new List<string>();
        var at = block;
        while (Marshal.PtrToStringUni(at) is { Length: > 0 } variable)
        {
            variables.Add(variable);
            at += (variable.Length + 1) * sizeof(char);
        }

        return variables;
    }

    /// A Unicode environment block: `name=value` strings, each ending in a null, sorted by name ignoring case as Windows
    /// asks, then one more null. `settings` replace variables of the same name (ignoring case) or are added.
    internal static string EnvironmentBlock(IEnumerable<string> variables, IEnumerable<KeyValuePair<string, string>> settings)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in variables)
        {
            // A name may start with '=' (the hidden per-drive folders), so its separator is the first '=' after that.
            var separator = variable.IndexOf('=', 1);
            if (separator > 0) byName[variable[..separator]] = variable;
        }

        foreach (var (name, value) in settings) byName[name] = $"{name}={value}";

        var block = new StringBuilder();
        foreach (var name in byName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            block.Append(byName[name]).Append('\0');
        return block.Append('\0').ToString();
    }

    /// The command line .NET's Process would build: the program in quotes, then each argument quoted only if it is
    /// empty or has spaces or quotes, with backslashes doubled before a quote, so the C runtime's parser gets it back.
    internal static string CommandLine(string exe, IReadOnlyList<string> arguments)
    {
        var line = new StringBuilder().Append('"').Append(exe).Append('"');
        foreach (var argument in arguments)
        {
            line.Append(' ');
            if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
            {
                line.Append(argument);
                continue;
            }

            line.Append('"');
            for (var i = 0; i < argument.Length;)
            {
                var c = argument[i++];
                if (c == '\\')
                {
                    var backslashes = 1;
                    while (i < argument.Length && argument[i] == '\\')
                    {
                        i++;
                        backslashes++;
                    }

                    if (i == argument.Length)
                    {
                        line.Append('\\', backslashes * 2); // before the closing quote
                    }
                    else if (argument[i] == '"')
                    {
                        line.Append('\\', backslashes * 2 + 1).Append('"');
                        i++;
                    }
                    else
                    {
                        line.Append('\\', backslashes);
                    }
                }
                else if (c == '"')
                {
                    line.Append("\\\"");
                }
                else
                {
                    line.Append(c);
                }
            }

            line.Append('"');
        }

        return line.ToString();
    }

    static uint Milliseconds(TimeSpan timeout) => (uint)Math.Clamp(timeout.TotalMilliseconds, 0, uint.MaxValue - 1);

    static void Kill(KernelHandle process, int processId)
    {
        if (WaitForSingleObject(process, 0) != WAIT_TIMEOUT) return; // it has exited

        try
        {
            // The open handle keeps the id from being reused, so this is the same process.
            using var child = Process.GetProcessById(processId);
            child.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            // Part of the tree couldn't be killed; at least the process itself is stopped below.
        }

        TerminateProcess(process, 1);
        WaitForSingleObject(process, 2000);
    }
}
