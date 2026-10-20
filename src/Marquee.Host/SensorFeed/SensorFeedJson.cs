using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Marquee.Host.SensorFeed;

/// The feed's messages as JSON (SensorFeedFormat describes the shape). Reading is strict: a message of another version,
/// or with anything missing or of the wrong kind, is a FormatException, which the client turns into "no feed".
static class SensorFeedJson
{
    /// Text as UTF-8, not escaped to \uXXXX: the default escaping makes each character outside ASCII six bytes, and
    /// custom values in another script would no longer fit under the cap. The message goes to a JSON reader only,
    /// never into HTML, which is what the default escaping guards against.
    static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static byte[] Write(SensorFeedValues message) => Write(message.Version, message.Now, message.Truncated, json =>
    {
        json.WriteStartObject("values");
        foreach (var (key, value) in message.Values.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            json.WriteStartObject(key);
            if (value.Number is { } number) json.WriteNumber("number", number);
            else json.WriteString("text", value.Text ?? "");
            json.WriteBoolean("stale", value.Stale);
            json.WriteEndObject();
        }

        json.WriteEndObject();
    });

    public static byte[] Write(SensorFeedHistory message) => Write(message.Version, message.Now, message.Truncated, json =>
    {
        json.WriteStartObject("history");
        foreach (var (key, points) in message.History.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            json.WriteStartArray(key);
            foreach (var point in points) json.WriteNumberValue(point);
            json.WriteEndArray();
        }

        json.WriteEndObject();
    });

    public static SensorFeedValues ReadValues(ReadOnlyMemory<byte> utf8) => Read(utf8, "values", (version, now, truncated, values) =>
    {
        var read = new Dictionary<string, FeedValue>(StringComparer.Ordinal);
        foreach (var entry in Object(values).EnumerateObject())
            read[entry.Name] = Value(entry);
        return new SensorFeedValues(version, now, read, truncated);
    });

    public static SensorFeedHistory ReadHistory(ReadOnlyMemory<byte> utf8) => Read(utf8, "history", (version, now, truncated, history) =>
    {
        var read = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);
        foreach (var entry in Object(history).EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Array) throw new FormatException($"history.{entry.Name} is not a list");
            read[entry.Name] = entry.Value.EnumerateArray().Select(point => Number(point, $"history.{entry.Name}")).ToArray();
        }

        return new SensorFeedHistory(version, now, read, truncated);
    });

    static byte[] Write(int version, DateTimeOffset now, bool truncated, Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, Options))
        {
            json.WriteStartObject();
            json.WriteNumber("version", version);
            json.WriteString("now", now);
            json.WriteBoolean("truncated", truncated);
            body(json);
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    static T Read<T>(ReadOnlyMemory<byte> utf8, string body, Func<int, DateTimeOffset, bool, JsonElement, T> read)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8);
            var root = Object(document.RootElement);
            var version = root.GetProperty("version").GetInt32();
            if (version != SensorFeedFormat.Version)
                throw new FormatException($"the message is version {version}, and this build reads {SensorFeedFormat.Version}");
            return read(version, root.GetProperty("now").GetDateTimeOffset(), root.GetProperty("truncated").GetBoolean(),
                root.GetProperty(body));
        }
        // What JsonElement throws for a missing property (KeyNotFoundException) or one of the wrong kind
        // (InvalidOperationException, FormatException), and JsonDocument for text that isn't JSON.
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException($"the message is malformed: {ex.Message}", ex);
        }
    }

    static FeedValue Value(JsonProperty entry)
    {
        var value = Object(entry.Value);
        var hasNumber = value.TryGetProperty("number", out var number);
        var hasText = value.TryGetProperty("text", out var text);
        if (hasNumber == hasText) throw new FormatException($"values.{entry.Name} needs a number or a text");
        var stale = value.GetProperty("stale").GetBoolean();
        return hasNumber
            ? new FeedValue(Number(number, $"values.{entry.Name}"), null, stale)
            : new FeedValue(null, Text(text, $"values.{entry.Name}"), stale);
    }

    /// A string; GetString would read null as null, and a value is a number or a text, never neither.
    static string Text(JsonElement element, string where) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : throw new FormatException($"{where} is not a text");

    static double Number(JsonElement element, string where) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number) && double.IsFinite(number)
            ? number
            : throw new FormatException($"{where} is not a number");

    static JsonElement Object(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object ? element : throw new FormatException("an object was expected");
}
