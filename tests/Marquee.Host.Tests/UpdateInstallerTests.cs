using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Marquee.Host.Tests.TestSupport;
using Marquee.Host.Updates;

namespace Marquee.Host.Tests;

public sealed class UpdateInstallerTests : IDisposable
{
    const string InstallerName = "Marquee-0.2.0-x64.msi";
    const string SignatureName = "Marquee-0.2.0-x64.msi.sig";

    static readonly Uri InstallerUrl = new($"{ReleaseJson.Downloads}/v0.2.0/{InstallerName}");
    static readonly Uri SignatureUrl = new($"{ReleaseJson.Downloads}/v0.2.0/{SignatureName}");

    readonly TempDir temp = new();
    readonly ListLog log = new();
    readonly FakeProcessStarter processes = new();
    readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    readonly byte[] installer = RandomNumberGenerator.GetBytes(200_000);

    public void Dispose()
    {
        key.Dispose();
        temp.Dispose();
    }

    string Folder => temp.Path;

    string Sign(byte[] file, ECDsa? with = null) => ReleaseSignature.Sign(new MemoryStream(file), with ?? key);

    UpdateOffer Offer(long? installerSize = null, long signatureSize = 104) => new(new Version(0, 2, 0), "Marquee 0.2.0", null,
        new ReleaseAsset(InstallerName, installerSize ?? installer.Length, InstallerUrl),
        new ReleaseAsset(SignatureName, signatureSize, SignatureUrl));

    /// A server with the installer and the signature given, or a 404 where one is null.
    static FakeHttpHandler Serving(byte[]? installerBytes, string? signature) => Serving(installerBytes,
        signature is null ? null : Encoding.ASCII.GetBytes(signature));

    static FakeHttpHandler Serving(byte[]? installerBytes, byte[]? signature) => new(request =>
    {
        var bytes = request.RequestUri == InstallerUrl ? installerBytes
            : request.RequestUri == SignatureUrl ? signature
            : null;
        return bytes is null ? FakeHttpHandler.Status(HttpStatusCode.NotFound) : FakeHttpHandler.Bytes(bytes);
    });

    UpdateInstaller Installer(FakeHttpHandler handler, ECDsa? releaseKey = null) =>
        new(handler.Client(), "0.1.0+4393804e1c2b", releaseKey ?? key, processes, log);

    Task<VerifiedUpdate> Download(FakeHttpHandler handler, UpdateOffer? offer = null, IProgress<long>? progress = null, ECDsa? releaseKey = null) =>
        Installer(handler, releaseKey).DownloadAsync(offer ?? Offer(), Folder, progress, CancellationToken.None);

    /// Downloading is refused with a message, logged once as an error, and leaves no installer behind.
    async Task<string> Refused(FakeHttpHandler handler, UpdateOffer? offer = null, ECDsa? releaseKey = null)
    {
        var error = await Assert.ThrowsAsync<UpdateException>(() => Download(handler, offer, releaseKey: releaseKey));

        Assert.Empty(processes.Started);
        Assert.Equal($"error: {error.Message}", Assert.Single(log.Entries));
        Assert.Empty(Directory.GetFiles(Folder, "*.msi*"));
        return error.Message;
    }

    [Fact]
    public async Task It_downloads_the_installer_and_its_signature_and_verifies_them()
    {
        var signature = Sign(installer);
        var handler = Serving(installer, signature);

        using var update = await Download(handler);

        Assert.Equal(Path.Combine(Folder, InstallerName), update.InstallerPath);
        Assert.Equal(new Version(0, 2, 0), update.Offer.Version);
        Assert.Equal(signature, File.ReadAllText(Path.Combine(Folder, SignatureName)));
        Assert.Equal([InstallerName, SignatureName], Directory.GetFiles(Folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal([SignatureUrl, InstallerUrl], handler.Requests.Select(request => request.Uri));
        Assert.All(handler.Requests, request => Assert.Equal("Marquee/0.1.0", request.UserAgent));
        Assert.Empty(processes.Started);

        using var held = new FileStream(update.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Assert.Equal(installer, ReadAll(held));
    }

    [Fact]
    public async Task Install_runs_msiexec_from_System32_passively_without_restarting_with_a_verbose_log_beside_the_installer()
    {
        using var update = await Download(Serving(installer, Sign(installer)));

        using var started = update.Install();

        var (fileName, arguments) = Assert.Single(processes.Started);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), fileName);
        var installerPath = Path.Combine(Folder, InstallerName);
        Assert.Equal($"/i \"{installerPath}\" /passive /norestart /l*v \"{installerPath}.log\"", arguments);
        Assert.Equal(installerPath + ".log", update.LogPath);
    }

    [Fact]
    public async Task Install_returns_msiexec_to_wait_for()
    {
        using var update = await Download(Serving(installer, Sign(installer)));

        using var started = update.Install();
        processes.Processes.Single().Exit(1603);

        Assert.Equal(1603, await started.WaitForExitAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3010, true)] // installed; a restart would finish it (/norestart)
    [InlineData(1641, true)] // installed, and a restart was started
    [InlineData(1602, false)] // the user cancelled
    [InlineData(1603, false)] // a fatal error
    [InlineData(1618, false)] // another installation is under way
    [InlineData(1638, false)] // another version is installed
    public void Only_msiexecs_success_codes_mean_it_installed(int exitCode, bool installed)
    {
        Assert.Equal(installed, VerifiedUpdate.Installed(exitCode));
    }

    [Fact]
    public async Task An_msiexec_that_cant_start_is_an_update_exception_logged_once()
    {
        var handler = Serving(installer, Sign(installer));
        var installerThatFails = new UpdateInstaller(handler.Client(), "0.1.0", key, new FakeProcessStarter(new Win32Exception(2)), log);
        using var update = await installerThatFails.DownloadAsync(Offer(), Folder, null, CancellationToken.None);

        var error = Assert.Throws<UpdateException>(update.Install);

        Assert.Equal($"error: {error.Message}", log.Entries[^1]);
    }

    [Fact]
    public async Task An_update_disposed_of_cant_be_installed()
    {
        var update = await Download(Serving(installer, Sign(installer)));
        update.Dispose();

        Assert.Throws<ObjectDisposedException>(update.Install);
        Assert.Empty(processes.Started);
    }

    [Fact]
    public async Task The_installer_stays_open_so_nothing_can_change_or_replace_it_until_the_update_is_disposed()
    {
        var update = await Download(Serving(installer, Sign(installer)));
        var path = update.InstallerPath;

        Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete));
        Assert.Throws<IOException>(() => File.Delete(path));
        Assert.Throws<IOException>(() => File.Move(path, path + ".moved"));
        // Reading, as msiexec does, is still allowed.
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            Assert.Equal(installer.Length, reader.Length);

        update.Dispose();

        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    }

    [Fact]
    public async Task A_tampered_installer_is_refused()
    {
        var signature = Sign(installer);
        var tampered = installer.ToArray();
        tampered[100_000] ^= 0x01;

        await Refused(Serving(tampered, signature));
    }

    [Fact]
    public async Task A_signature_from_another_key_is_refused()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await Refused(Serving(installer, Sign(installer, other)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("marquee-sig-v1 not-base64\n")]
    [InlineData("-----BEGIN PGP SIGNATURE-----\n")]
    public async Task A_malformed_signature_is_refused(string signature)
    {
        await Refused(Serving(installer, signature));
    }

    [Fact]
    public async Task A_missing_signature_is_refused_before_the_installer_is_downloaded()
    {
        var handler = Serving(installer, signature: (string?)null);

        Assert.Contains(SignatureName, await Refused(handler));
        Assert.DoesNotContain(handler.Requests, request => request.Uri == InstallerUrl);
        Assert.Empty(Directory.GetFiles(Folder));
    }

    [Fact]
    public async Task A_signature_over_1_KB_is_refused()
    {
        var signature = Encoding.ASCII.GetBytes(Sign(installer) + new string(' ', 1024));

        await Refused(Serving(installer, signature));
    }

    [Fact]
    public async Task An_installer_larger_than_the_release_said_is_refused()
    {
        await Refused(Serving([.. installer, 0], Sign([.. installer, 0])));
    }

    [Fact]
    public async Task An_installer_smaller_than_the_release_said_is_refused()
    {
        await Refused(Serving(installer[..^1], Sign(installer[..^1])));
    }

    [Fact]
    public async Task An_offer_over_300_MB_is_refused_without_downloading()
    {
        var handler = Serving(installer, Sign(installer));

        await Refused(handler, Offer(installerSize: 300L * 1024 * 1024 + 1));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task With_the_placeholder_key_nothing_is_downloaded_or_installed()
    {
        using var placeholder = PlaceholderKey.Open();
        var handler = Serving(installer, Sign(installer));

        await Refused(handler, releaseKey: placeholder);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_failed_download_is_an_update_exception()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable));

        await Refused(handler);
    }

    [Fact]
    public async Task Old_downloads_are_cleaned_up_first_and_nothing_else()
    {
        File.WriteAllText(Path.Combine(Folder, "Marquee-0.1.0-x64.msi"), "old");
        File.WriteAllText(Path.Combine(Folder, "Marquee-0.1.0-x64.msi.sig"), "old");
        File.WriteAllText(Path.Combine(Folder, "Marquee-0.1.0-x64.msi.log"), "msiexec's log of the last update");
        File.WriteAllText(Path.Combine(Folder, "Marquee-0.2.0-x64.msi.0123456789abcdef.partial"), "half");
        File.WriteAllText(Path.Combine(Folder, "notes.txt"), "not Marquee's");

        using var update = await Download(Serving(installer, Sign(installer)));

        Assert.Equal([InstallerName, SignatureName, "notes.txt"],
            Directory.GetFiles(Folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_refused_download_cleans_up_old_downloads_too()
    {
        File.WriteAllText(Path.Combine(Folder, "Marquee-0.1.0-x64.msi"), "old");
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await Refused(Serving(installer, Sign(installer, other)));

        Assert.Empty(Directory.GetFiles(Folder));
    }

    [Fact]
    public async Task Progress_counts_the_installers_bytes()
    {
        var progress = new RecordingProgress();

        using var update = await Download(Serving(installer, Sign(installer)), progress: progress);

        Assert.Equal(installer.Length, progress.Values[^1]);
        Assert.Equal(progress.Values.Order(), progress.Values);
    }

    [Fact]
    public async Task Cancelling_cancels_and_leaves_nothing_behind()
    {
        using var cancel = new CancellationTokenSource();
        var handler = new FakeHttpHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri == SignatureUrl) return FakeHttpHandler.Bytes(Encoding.ASCII.GetBytes(Sign(installer)));
            cancel.Cancel();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Installer(handler).DownloadAsync(Offer(), Folder, null, cancel.Token));
        Assert.Empty(Directory.GetFiles(Folder));
        Assert.Empty(processes.Started);
    }

    static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// IProgress that records each report as it is made, unlike Progress<T>, which posts them for later.
    sealed class RecordingProgress : IProgress<long>
    {
        public List<long> Values { get; } = [];

        public void Report(long value)
        {
            lock (Values) Values.Add(value);
        }
    }
}
