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

    public static string? String(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null; // text that isn't valid UTF-16 (a lone surrogate escape) reads as missing
        }
    }

    /// True when the field exists and is not JSON null.
    public static bool Has(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

    // Unix seconds up to the year 2286; later times are treated as garbage.
    const double MaxUnixSeconds = 10_000_000_000;

    /// True for a time after 1970 and before the year 2286: anything else is not a real timestamp.
    internal static bool IsPlausibleUnixSeconds(double seconds) => seconds is > 0 and <= MaxUnixSeconds;

    /// A point in time as Unix seconds: a number of seconds (or milliseconds, if it is too large to be seconds),
    /// or an ISO 8601 string. Times that are zero, negative or absurdly far away read as null.
    public static double? UnixSeconds(JsonElement parent, string name)
    {
        double? seconds;
        if (Number(parent, name) is { } number)
            seconds = number > 100_000_000_000 ? number / 1000 : number;
        else
            seconds = String(parent, name) is { } text
                      && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
                ? time.ToUnixTimeMilliseconds() / 1000.0
                : null;
        return seconds is { } value && IsPlausibleUnixSeconds(value) ? seconds : null;
    }
}
