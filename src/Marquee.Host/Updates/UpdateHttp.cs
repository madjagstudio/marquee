using System.Net.Http.Headers;

namespace Marquee.Host.Updates;

/// The updater's requests: the client the tray makes for them, and what each one sends.
public static class UpdateHttp
{
    /// A client for UpdateChecker and UpdateInstaller. It has no timeout of its own, since downloading an installer on a
    /// slow line can take minutes: the checker gives up after 15 seconds and a download when it stalls. It keeps no
    /// cookies, and follows redirects, which GitHub's download links are (.NET never follows one from https to http).
    /// It uses the system's proxy settings, as every other app on the PC does.
    public static HttpClient CreateClient() => new(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5, UseCookies = false })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// A GET that says it is Marquee and which version, "Marquee/1.2.3", as GitHub's API requires of every request (it
    /// refuses one without a User-Agent), and nothing else about the PC or its user: no cookies, no token.
    internal static HttpRequestMessage Get(Uri uri, string runningVersion, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(ReleaseVersion.TryParseRunning(runningVersion, out var running)
            ? new ProductInfoHeaderValue("Marquee", running.ToString())
            : new ProductInfoHeaderValue("Marquee", null));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return request;
    }

    /// The body, or null when it is longer than `maxBytes`, which is all that is read of it.
    internal static async Task<byte[]?> ReadAtMost(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBytes) return null;

        await using var body = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maxBytes + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await body.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
            length += read;
        return length > maxBytes ? null : buffer[..length];
    }
}
