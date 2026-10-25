using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Marquee.Host.Updates;

namespace Marquee.App.Tests.TestSupport;

/// GitHub as the updater sees it, with nothing on the network: an HttpMessageHandler that answers the latest-release
/// request with a release of version Latest, and serves that release's installer and its signature, made with Key.
/// Nothing here opens a socket.
sealed class FakeGitHub : HttpMessageHandler
{
    public const string Downloads = "https://github.com/madjagstudio/marquee/releases/download";

    int _apiRequests;
    int _installerRequests;

    public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public string Latest { get; set; } = "0.2.0";

    public byte[] Installer { get; } = RandomNumberGenerator.GetBytes(100_000);

    /// Thrown at the latest-release request, as a network that can't reach GitHub does.
    public Exception? ApiFailure { get; set; }

    /// The signature served is for other bytes, as for a tampered installer.
    public bool TamperedInstaller { get; set; }

    /// When set, the installer's body stops halfway until this completes, and waits for cancellation if it never does.
    public TaskCompletionSource? HoldInstallerHalfway { get; set; }

    public int ApiRequests => Volatile.Read(ref _apiRequests);

    public int InstallerRequests => Volatile.Read(ref _installerRequests);

    /// A client over this handler, with no timeout of its own, as UpdateHttp.CreateClient makes the real one.
    public HttpClient Client() => new(this, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

    public static string ReleasePage(string version) => $"https://github.com/madjagstudio/marquee/releases/tag/v{version}";

    static string InstallerName(string version) => $"Marquee-{version}-x64.msi";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!.ToString();
        if (uri == ReleaseFeed.LatestReleaseUrl)
        {
            Interlocked.Increment(ref _apiRequests);
            if (ApiFailure is { } failure) throw failure;
            return Task.FromResult(Json(ReleaseJson()));
        }

        if (uri == $"{Downloads}/v{Latest}/{InstallerName(Latest)}.sig")
        {
            var signed = TamperedInstaller ? RandomNumberGenerator.GetBytes(Installer.Length) : Installer;
            return Task.FromResult(Bytes(Encoding.ASCII.GetBytes(ReleaseSignature.Sign(new MemoryStream(signed), Key))));
        }

        if (uri == $"{Downloads}/v{Latest}/{InstallerName(Latest)}")
        {
            Interlocked.Increment(ref _installerRequests);
            HttpContent content = HoldInstallerHalfway is { } hold
                ? new StreamContent(new HeldStream(Installer, hold.Task)) { Headers = { ContentLength = Installer.Length } }
                : new ByteArrayContent(Installer);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Key.Dispose();
        base.Dispose(disposing);
    }

    string ReleaseJson() => new JsonObject
    {
        ["html_url"] = ReleasePage(Latest),
        ["tag_name"] = $"v{Latest}",
        ["name"] = $"Marquee {Latest}",
        ["draft"] = false,
        ["prerelease"] = false,
        ["assets"] = new JsonArray(
            Asset(InstallerName(Latest), Installer.Length),
            Asset(InstallerName(Latest) + ".sig", 104)),
    }.ToJsonString();

    JsonObject Asset(string name, long size) => new()
    {
        ["name"] = name,
        ["size"] = size,
        ["browser_download_url"] = $"{Downloads}/v{Latest}/{name}",
    };

    static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    static HttpResponseMessage Bytes(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } } };

    /// The first half of `bytes`, then the rest once `release` completes.
    sealed class HeldStream(byte[] bytes, Task release) : Stream
    {
        int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var half = bytes.Length / 2;
            if (_position >= half) await release.WaitAsync(cancellationToken);
            var end = _position < half ? half : bytes.Length;
            var count = Math.Min(buffer.Length, end - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
