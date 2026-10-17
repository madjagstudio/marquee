using System.Globalization;
using System.Text.Json;

namespace Marquee.Providers.Agents;

/// Lenient reads from JSON written by other programs. A field that is missing or has the wrong type reads as null;
/// callers decide which missing fields mean the format has changed.
static class JsonFields
{
    public static JsonElement? Object(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    public static double? Number(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number
            : null;

    public static string? String(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// True when the field exists and is not JSON null.
    public static bool Has(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

    /// A point in time as Unix seconds: a number of seconds (or milliseconds, if it is too large to be seconds),
    /// or an ISO 8601 string.
    public static double? UnixSeconds(JsonElement parent, string name)
    {
        if (Number(parent, name) is { } number)
            return number > 100_000_000_000 ? number / 1000 : number;
        return String(parent, name) is { } text
               && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time.ToUnixTimeMilliseconds() / 1000.0
            : null;
    }
}
