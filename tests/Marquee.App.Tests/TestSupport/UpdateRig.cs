using System.Collections.Concurrent;
using System.Security.Cryptography;
using Marquee.Core.Logging;
using Marquee.Host.Updates;

namespace Marquee.App.Tests.TestSupport;

/// TrayUpdates as the tray makes it, over a FakeGitHub instead of the network, with a fake clock and delay, msiexec
/// faked, and its state file and download folder in `dir`. The running Marquee is 0.1.0.
sealed class UpdateRig : IDisposable
{
    public const string Running = "0.1.0";

    readonly HttpClient _client;
    readonly ECDsa? _placeholder;

    /// `placeholderKey`: the build carries the placeholder release key, as every build does before the key ceremony.
    public UpdateRig(TempDir dir, bool placeholderKey = false)
    {
        _client = GitHub.Client();
        _placeholder = placeholderKey ? PlaceholderKey.Open() : null;
        var key = _placeholder ?? GitHub.Key;
        const string running = Running + "+4393804e1c2b";
        StateFile = dir.File(Path.Combine("data", UpdateSchedule.FileName));
        Downloads = dir.File("updates");
        Updates = new TrayUpdates(
            new UpdateChecker(_client, running, key, NullLog.Instance, UpdateChecker.DefaultTimeout),
            new UpdateInstaller(_client, running, key, Processes, NullLog.Instance),
            StateFile, Downloads,
            folder =>
            {
                SecuredFolders.Enqueue(folder);
                Directory.CreateDirectory(folder);
            },
            NullLog.Instance, () => Now, Delay.Wait);
    }

    public FakeGitHub GitHub { get; } = new();

    public FakeDelay Delay { get; } = new();

    public FakeProcessStarter Processes { get; } = new();

    /// The fake clock. Set it before what reads it is triggered.
    public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public string StateFile { get; }

    public string Downloads { get; }

    /// The folders TrayUpdates asked to have made admin-only (here they are only made).
    public ConcurrentQueue<string> SecuredFolders { get; } = new();

    public TrayUpdates Updates { get; }

    public void Dispose()
    {
        Updates.Dispose();
        _client.Dispose();
        GitHub.Dispose();
        _placeholder?.Dispose();
    }
}
