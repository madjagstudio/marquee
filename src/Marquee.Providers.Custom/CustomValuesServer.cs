using System.Net;
using System.Text;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Custom;

/// `POST /values` on 127.0.0.1, for scripts and `marquee push`. Only this PC may post, and web pages can't:
/// - Windows' HTTP server routes a "localhost" prefix by Host header on every network interface, so a LAN client
///   sending `Host: localhost` would get in. Marquee registers only the 127.0.0.1 prefix and also refuses any request
///   whose remote address isn't loopback.
/// - Through loopback, Windows accepts any Host header, so a DNS-rebinding page would get in. Marquee refuses any Host
///   but 127.0.0.1 or localhost.
/// - A request with an Origin header (sent by browsers) is refused, and JSON content is required, which a page can't
///   send cross-site without a preflight this server never approves.
public sealed class CustomValuesServer(SensorStore store, ILog log, int port = CustomValuesServer.DefaultPort, int maxKeys = 1000) : IDisposable
{
    public const int DefaultPort = 47913;
    public const int MaxBodyBytes = 64 * 1024;
    internal static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

    readonly HttpListener _listener = new();
    readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    volatile bool _stopping;
    Task? _loop;

    public int Port => port;

    /// Starts listening. False (with an error logged) if the port can't be used; the rest of Marquee runs on.
    public bool Start()
    {
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try
        {
            // One stalled local client must not hold up everyone else's pushes for minutes.
            _listener.TimeoutManager.EntityBody = ClientTimeout;
            _listener.TimeoutManager.HeaderWait = ClientTimeout;
        }
        catch (PlatformNotSupportedException)
        {
            // Only Windows' HTTP server has these limits; elsewhere the defaults apply.
        }

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            log.Error($"Custom values are unavailable: 127.0.0.1:{port} can't be used ({ex.Message}). " +
                "Use --http-port to pick another port, and pass the same --port to marquee push.");
            return false;
        }

        _loop = Task.Run(Listen);
        return true;
    }

    public void Dispose()
    {
        _stopping = true;
        try
        {
            if (_listener.IsListening) _listener.Stop();
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Listen reports its own failures; shutting down never throws.
        }
        finally
        {
            _listener.Close();
        }
    }

    async Task Listen()
    {
        while (!_stopping)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex)
            {
                // Stopping a listener fails a pending GetContextAsync with one of several exceptions (even an
                // ArgumentException about an invalid handle), so any exception while stopping is the normal end.
                if (!_stopping) log.Error($"Custom values stopped working: {ex.Message}");
                return;
            }

            try
            {
                var (status, message) = Handle(context.Request);
                Respond(context.Response, status, message);
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
            {
                // The client hung up, or Marquee is stopping; nothing to tell anyone.
            }
            catch (Exception ex)
            {
                // Nothing a request provokes may stop the loop: say so, answer 500 if it can still be answered, go on.
                log.Error($"Custom values: a request failed: {ex.Message}");
                try
                {
                    Respond(context.Response, 500, "Marquee could not handle the request.");
                }
                catch
                {
                    // The answer failed too (the client is gone); the loop carries on.
                }
            }
        }
    }

    (int Status, string? Message) Handle(HttpListenerRequest request)
    {
        if (!IPAddress.IsLoopback(request.RemoteEndPoint.Address)) return (403, "Only this PC can send values.");
        if (!IsLocalHost(request.Headers["Host"])) return (400, "Address requests to 127.0.0.1 or localhost.");
        if (request.Url?.AbsolutePath != "/values") return (404, "Not found. POST JSON to /values.");
        if (request.HttpMethod != "POST") return (405, "Use POST.");
        if (request.Headers["Origin"] is not null) return (403, "Requests from web pages are refused.");
        if (request.ContentType?.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase) != true)
            return (415, "Send JSON with Content-Type: application/json.");
        if (request.ContentLength64 > MaxBodyBytes) return (413, $"The body is larger than {MaxBodyBytes / 1024} KB.");

        var body = ReadBody(request.InputStream);
        if (body is null) return (413, $"The body is larger than {MaxBodyBytes / 1024} KB.");

        CustomUpdate update;
        try
        {
            update = CustomValues.Parse(body);
        }
        catch (CustomValuesException ex)
        {
            return (400, ex.Message);
        }

        lock (_keys)
        {
            var added = update.Values.Select(v => v.Key).Where(k => !_keys.Contains(k)).Distinct().Count();
            if (_keys.Count + added > maxKeys)
                return (400, $"Marquee holds at most {maxKeys} different custom keys; restart it to clear them.");
            foreach (var (key, _) in update.Values) _keys.Add(key);
        }

        update.Apply(store);
        return (204, null);
    }

    /// "127.0.0.1" or "localhost", with or without a port.
    internal static bool IsLocalHost(string? host)
    {
        if (host is null) return false;
        var colon = host.LastIndexOf(':');
        var name = colon >= 0 ? host[..colon] : host;
        return name.Equals("127.0.0.1", StringComparison.Ordinal) || name.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    static string? ReadBody(Stream input)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = input.Read(chunk)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    static void Respond(HttpListenerResponse response, int status, string? message)
    {
        response.StatusCode = status;
        if (status == 405) response.AddHeader("Allow", "POST");
        if (message is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(message + "\n");
            response.ContentType = "text/plain; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes);
        }

        response.Close();
    }
}
