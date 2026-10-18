using System.Text.Json;
using System.Text.RegularExpressions;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Custom;

/// Why a pushed update was refused, in words for whoever sent it.
public sealed class CustomValuesException(string message) : Exception(message);

/// Values to write, all with the same ttl (none: they never go stale).
public sealed record CustomUpdate(IReadOnlyList<KeyValuePair<string, SensorValue>> Values, TimeSpan? Ttl)
{
    public void Apply(SensorStore store)
    {
        foreach (var (key, value) in Values)
        {
            if (value.Number is { } number) store.Set(key, number, Ttl);
            else store.Set(key, value.Text!, Ttl);
        }
    }
}

/// The JSON body of `POST /values`: `{"custom.build": "ok", "custom.queue": 12, "ttl": "30s"}`. Keys start with
/// "custom." and are made of the characters theme expressions accept in a key; values are numbers or short strings;
/// `ttl` is seconds or a duration such as "30s", "5m", "2h". An update is validated whole before anything is
/// written; the writes are then made one key at a time, so a frame drawn meanwhile may see part of an update.
public static partial class CustomValues
{
    public const string Prefix = "custom.";
    public const int MaxKeyLength = 64;
    public const int MaxTextLength = 256;
    public const int MaxKeysPerUpdate = 100;

    public static bool IsValidKey(string key) => key.Length <= MaxKeyLength && KeyPattern().IsMatch(key);

    public static CustomUpdate Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CustomValuesException($"The body is not valid JSON: {ex.Message}");
        }

        using (document)
        try
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new CustomValuesException("The body must be a JSON object such as {\"custom.build\": \"ok\", \"ttl\": \"30s\"}.");

            TimeSpan? ttl = null;
            var values = new List<KeyValuePair<string, SensorValue>>();
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name == "ttl")
                {
                    ttl = Ttl(property.Value);
                    continue;
                }

                if (!IsValidKey(property.Name))
                    throw new CustomValuesException($"'{property.Name}' is not a custom key. Keys start with \"{Prefix}\", use letters, digits, " +
                        $"_ and dots, and are at most {MaxKeyLength} characters, e.g. \"custom.build\".");
                values.Add(new(property.Name, Value(property.Name, property.Value)));
            }

            if (values.Count == 0) throw new CustomValuesException("The body has no values to set.");
            if (values.Count > MaxKeysPerUpdate) throw new CustomValuesException($"At most {MaxKeysPerUpdate} values can be set at once.");
            return new CustomUpdate(values, ttl);
        }
        catch (InvalidOperationException ex)
        {
            // Text that parses as JSON but isn't valid UTF-16 (a lone surrogate escape) throws when it is read.
            throw new CustomValuesException($"The body is not valid JSON text: {ex.Message}");
        }
    }

    static SensorValue Value(string key, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDouble(out var number) && double.IsFinite(number) => SensorValue.Of(number),
        JsonValueKind.Number => throw new CustomValuesException($"{key}: numbers must be finite."),
        JsonValueKind.String when value.GetString()!.Length <= MaxTextLength => SensorValue.Of(value.GetString()!),
        JsonValueKind.String => throw new CustomValuesException($"{key}: text values are at most {MaxTextLength} characters."),
        _ => throw new CustomValuesException($"{key}: values must be numbers or text, not {value.ValueKind.ToString().ToLowerInvariant()}."),
    };

    static TimeSpan Ttl(JsonElement value)
    {
        var ok = value.ValueKind switch
        {
            JsonValueKind.Number => DurationText.FromSeconds(value.GetDouble(), out var seconds) ? seconds : (TimeSpan?)null,
            JsonValueKind.String => DurationText.TryParse(value.GetString(), out var text) ? text : null,
            _ => null,
        };
        return ok ?? throw new CustomValuesException("ttl must be a positive number of seconds or a duration such as \"30s\", \"5m\" or \"2h\" (at most a year).");
    }

    [GeneratedRegex(@"^custom(\.[A-Za-z0-9_]+)+$")]
    private static partial Regex KeyPattern();
}
