using System.Net;
using System.Net.Sockets;
using System.Text;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Providers.Custom;

namespace Marquee.Providers.Custom.Tests;

public sealed class CustomValuesServerTests : IDisposable
{
    readonly SensorStore _store = new();
    readonly CustomValuesServer _server;
    readonly HttpClient _http = new();

    public CustomValuesServerTests()
    {
        _server = new CustomValuesServer(_store, NullLog.Instance, FreePort(), maxKeys: 5);
        Assert.True(_server.Start());
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    string Url(string path = "/values", string host = "127.0.0.1") => $"http://{host}:{_server.Port}{path}";

    HttpResponseMessage Post(string body, string contentType = "application/json", string? origin = null, string? url = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url ?? Url()) { Content = new StringContent(body, Encoding.UTF8) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return _http.Send(request);
    }

    object? Read(string key) => _store.Snapshot().TryGet(key, out var v) ? v.Number is { } n ? n : v.Text : null;

    [Fact]
    public void Post_sets_values()
    {
        using var response = Post("""{"custom.build":"ok","custom.queue":7}""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("ok", Read("custom.build"));
        Assert.Equal(7.0, Read("custom.queue"));
    }

    [Fact]
    public void Localhost_works_too()
    {
        using var response = Post("""{"custom.a":1}""", url: Url(host: "localhost"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public void Push_client_reaches_the_server()
    {
        Assert.Null(CustomValuesClient.Push(_server.Port, "custom.temp", "41.5", TimeSpan.FromMinutes(1)));

        Assert.Equal(41.5, Read("custom.temp"));
    }

    [Fact]
    public void Push_client_explains_a_refusal()
    {
        var error = CustomValuesClient.Push(_server.Port, "cpu.temp", "1", null);

        Assert.StartsWith("Marquee refused the value: 'cpu.temp' is not a custom key.", error);
    }

    [Fact]
    public void Push_client_waits_out_another_clients_stall()
    {
        // The server handles one request at a time and gives a stalled client the full body timeout, so a push queued
        // behind one needs that long plus its own.
        Assert.True(CustomValuesClient.Timeout >= 2 * CustomValuesServer.ClientTimeout + TimeSpan.FromSeconds(5),
            $"push gives up after {CustomValuesClient.Timeout}, the server allows {CustomValuesServer.ClientTimeout} per client");
    }

    [Fact]
    public void Push_client_explains_that_nothing_is_listening() =>
        Assert.StartsWith("Marquee isn't running", CustomValuesClient.Push(FreePort(), "custom.a", "1", null));

    [Fact]
    public async Task Bad_json_is_a_400_with_the_reason()
    {
        using var response = Post("""{"custom.a":true}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("custom.a: values must be numbers or text, not true.\n", await response.Content.ReadAsStringAsync());
        Assert.Null(Read("custom.a"));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public void Other_content_types_are_refused(string contentType)
    {
        using var response = Post("""{"custom.a":1}""", contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Null(Read("custom.a"));
    }

    [Fact]
    public void Requests_from_web_pages_are_refused()
    {
        using var response = Post("""{"custom.a":1}""", origin: "https://example.com");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(Read("custom.a"));
    }

    [Fact]
    public void Requests_for_other_host_names_are_refused()
    {
        // What a DNS-rebinding page would send: the right address, but its own host name.
        using var client = new TcpClient("127.0.0.1", _server.Port);
        using var stream = client.GetStream();
        var body = """{"custom.a":1}""";
        stream.Write(Encoding.ASCII.GetBytes($"POST /values HTTP/1.1\r\nHost: attacker.example:{_server.Port}\r\nContent-Type: application/json\r\n" +
            $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"));
        var reply = new StreamReader(stream).ReadToEnd();

        Assert.StartsWith("HTTP/1.1 400", reply);
        Assert.Null(Read("custom.a"));
    }

    [Fact]
    public void A_bad_request_does_not_stop_the_server()
    {
        // A lone surrogate escape parses as JSON but throws when read as text.
        using (var bad = Post("""{"custom.a":"\uD800"}""")) Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        using var good = Post("""{"custom.b":2}""");

        Assert.Equal(HttpStatusCode.NoContent, good.StatusCode);
        Assert.Equal(2.0, Read("custom.b"));
        // The fixture's Dispose must not throw either.
    }

    [Fact]
    public async Task Requests_from_the_network_are_refused()
    {
        // Windows routes `Host: localhost` to a localhost listener on every interface; this PC's own LAN address
        // stands in for another machine. With only the 127.0.0.1 prefix the connection is refused outright; if it
        // ever gets through, the request must still fail. (Skipped in effect on a PC with no IPv4 network.)
        var lan = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        if (lan is null) return;

        foreach (var host in new[] { "localhost", "127.0.0.1" })
        {
            using var client = new TcpClient();
            try
            {
                // A firewalled PC drops the connection instead of refusing it, so don't wait for the OS timeout.
                var connect = client.ConnectAsync(lan, _server.Port);
                if (await Task.WhenAny(connect, Task.Delay(3000)) != connect) continue; // unreachable
                await connect;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionRefused or SocketError.TimedOut)
            {
                continue;
            }

            using var stream = client.GetStream();
            var body = """{"custom.a":1}""";
            stream.Write(Encoding.ASCII.GetBytes($"POST /values HTTP/1.1\r\nHost: {host}:{_server.Port}\r\nContent-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"));

            Assert.Matches("^HTTP/1.1 (400|403|404) ", new StreamReader(stream).ReadLine());
        }

        Assert.Null(Read("custom.a"));
    }

    [Theory]
    [InlineData("127.0.0.1:47913", true)]
    [InlineData("localhost:47913", true)]
    [InlineData("LocalHost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("attacker.example:47913", false)]
    [InlineData("127.0.0.1.attacker.example:47913", false)]
    [InlineData("[::1]:47913", false)]
    [InlineData(null, false)]
    public void Host_names(string? host, bool allowed) => Assert.Equal(allowed, CustomValuesServer.IsLocalHost(host));

    [Fact]
    public async Task Wrong_paths_and_methods_are_refused()
    {
        using var notFound = await _http.GetAsync(Url("/"));
        using var wrongMethod = await _http.GetAsync(Url());

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        Assert.Equal("POST", wrongMethod.Content.Headers.Allow.Single());
    }

    [Fact]
    public void Large_bodies_are_refused()
    {
        using var response = Post("{\"custom.a\":\"" + new string('x', CustomValuesServer.MaxBodyBytes) + "\"}");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public void The_number_of_different_keys_is_capped()
    {
        using (var first = Post("""{"custom.k1":1,"custom.k2":2,"custom.k3":3,"custom.k4":4}""")) Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using (var again = Post("""{"custom.k1":5,"custom.k5":5}""")) Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);

        using var tooMany = Post("""{"custom.k6":6}""");

        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Null(Read("custom.k6"));
    }

    [Fact]
    public async Task A_key_stale_for_the_grace_period_frees_its_place()
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var store = new SensorStore(() => now);
        using var server = new CustomValuesServer(store, NullLog.Instance, FreePort(), maxKeys: 3);
        Assert.True(server.Start());
        HttpResponseMessage Push(string body) => Post(body, url: $"http://127.0.0.1:{server.Port}/values");

        using (var full = Push("""{"custom.k1":1,"custom.k2":2,"custom.k3":3,"ttl":"30s"}""")) Assert.Equal(HttpStatusCode.NoContent, full.StatusCode);
        now = now.AddSeconds(30) + CustomValuesServer.StaleGrace - TimeSpan.FromTicks(1);
        using (var refused = Push("""{"custom.k4":4}"""))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("Marquee holds at most 3 different custom keys. A key frees its place once it has been stale for 10 " +
                "minutes; restarting Marquee clears them all.\n", await refused.Content.ReadAsStringAsync());
        }

        now = now.AddTicks(1);
        using var accepted = Push("""{"custom.k4":4}""");

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(["custom.k4"], store.Snapshot().Keys);
    }

    [Fact]
    public void Keys_without_a_ttl_keep_their_place()
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var store = new SensorStore(() => now);
        using var server = new CustomValuesServer(store, NullLog.Instance, FreePort(), maxKeys: 3);
        Assert.True(server.Start());
        HttpResponseMessage Push(string body) => Post(body, url: $"http://127.0.0.1:{server.Port}/values");

        using (var forever = Push("""{"custom.k1":1,"custom.k2":2}""")) Assert.Equal(HttpStatusCode.NoContent, forever.StatusCode);
        using (var brief = Push("""{"custom.k3":3,"ttl":1}""")) Assert.Equal(HttpStatusCode.NoContent, brief.StatusCode);
        now = now.AddDays(30);

        using (var replaces = Push("""{"custom.k4":4}""")) Assert.Equal(HttpStatusCode.NoContent, replaces.StatusCode);
        using var refused = Push("""{"custom.k5":5}""");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(["custom.k1", "custom.k2", "custom.k4"], store.Snapshot().Keys);
    }

    [Fact]
    public void A_new_server_counts_the_custom_keys_already_in_the_store()
    {
        // A settings change starts a new server on the same store, which still holds every key pushed so far.
        var store = new SensorStore();
        foreach (var key in new[] { "custom.k1", "custom.k2", "custom.k3" }) store.Set(key, 1);
        using var server = new CustomValuesServer(store, NullLog.Instance, FreePort(), maxKeys: 3);
        Assert.True(server.Start());
        HttpResponseMessage Push(string body) => Post(body, url: $"http://127.0.0.1:{server.Port}/values");

        using (var refused = Push("""{"custom.k4":4}""")) Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var update = Push("""{"custom.k1":5}""");

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal(["custom.k1", "custom.k2", "custom.k3"], store.Snapshot().Keys);
    }

    [Fact]
    public void A_lowered_limit_still_lets_the_keys_already_held_be_set()
    {
        var store = new SensorStore();
        foreach (var key in new[] { "custom.k1", "custom.k2", "custom.k3" }) store.Set(key, 1);
        using var server = new CustomValuesServer(store, NullLog.Instance, FreePort(), maxKeys: 2);
        Assert.True(server.Start());
        HttpResponseMessage Push(string body) => Post(body, url: $"http://127.0.0.1:{server.Port}/values");

        using (var update = Push("""{"custom.k3":7}""")) Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        using var refused = Push("""{"custom.k4":4}""");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.True(store.Snapshot().TryGet("custom.k3", out var k3));
        Assert.Equal(7, k3.Number);
    }

    [Fact]
    public void A_busy_port_is_reported_not_thrown()
    {
        var log = new Recorder();
        using var second = new CustomValuesServer(new SensorStore(), log, _server.Port);

        Assert.False(second.Start());
        Assert.Contains("can't be used", Assert.Single(log.Errors));
    }

    sealed class Recorder : ILog
    {
        public List<string> Errors { get; } = [];
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) => Errors.Add(message);
    }
}
