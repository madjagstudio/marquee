using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Marquee.Providers.Custom;

/// `marquee push`: sends one value to a running Marquee's custom-values endpoint.
public static class CustomValuesClient
{
    /// The update as JSON. A value that reads as a number is sent as one; anything else is text.
    public static string Body(string key, string value, TimeSpan? ttl)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
                json.WriteNumber(key, number);
            else
                json.WriteString(key, value);
            if (ttl is { } t) json.WriteNumber("ttl", t.TotalSeconds);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// Null on success, or what went wrong in words.
    public static string? Push(int port, string key, string value, TimeSpan? ttl)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var content = new StringContent(Body(key, value, ttl), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        try
        {
            using var response = http.PostAsync($"http://127.0.0.1:{port}/values", content).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode) return null;
            var reason = response.Content.ReadAsStringAsync().GetAwaiter().GetResult().Trim();
            return $"Marquee refused the value: {(reason.Length > 0 ? reason : response.ReasonPhrase)}";
        }
        catch (HttpRequestException)
        {
            return $"Marquee isn't running, or isn't listening on 127.0.0.1:{port}. Start `marquee run` first (and match its --http-port).";
        }
        catch (TaskCanceledException)
        {
            return $"Marquee didn't answer on 127.0.0.1:{port} within 5 s.";
        }
    }
}
