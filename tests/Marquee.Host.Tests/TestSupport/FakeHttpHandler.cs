using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Marquee.Host.Tests.TestSupport;

/// What a request carried, copied as it was sent.
sealed record SentRequest(HttpMethod Method, Uri Uri, string UserAgent, string Accept, bool HasAuthorization);

/// Stands in for the network under an HttpClient: records each request and answers with `respond`, which can wait on the
/// request's cancellation token to play a server that never answers. Nothing here opens a socket.
sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    readonly List<SentRequest> _requests = [];

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public IReadOnlyList<SentRequest> Requests
    {
        get
        {
            lock (_requests) return _requests.ToList();
        }
    }

    /// An HttpClient over this handler, with no timeout of its own, as UpdateHttp.CreateClient makes the real one.
    public HttpClient Client() => new(this, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } } };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent("") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requests)
            _requests.Add(new SentRequest(request.Method, request.RequestUri!, request.Headers.UserAgent.ToString(),
                request.Headers.Accept.ToString(), request.Headers.Authorization is not null));
        return respond(request, cancellationToken);
    }
}
