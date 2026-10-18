using System.Text.Json;

namespace Marquee.Providers.Agents.Claude;

/// What `claude -p /usage --output-format stream-json --verbose` prints. Undocumented beyond the SDK's message types;
/// observed on 2026-10-18 (Claude Code 2.1.299): one JSON object per line, `system` lines, then an `assistant` line
/// whose `message.usage_report.rate_limits.limits` lists `{kind, percent, resets_at, ...}` entries (`session` is the
/// 5-hour window, `weekly_all` the 7-day one, `weekly_scoped` is per model), then a `result` line with the text `/usage` shows.
public static class ClaudeUsageReport
{
    /// How much of the `result` text goes into an error message, so "Not logged in" shows in the log.
    const int ResultTextLength = 120;

    /// Throws FormatException when no line carries a `usage_report` (the message says what the `result` line said), or
    /// when a limit Marquee reads is there but not in the observed shape (a format change). An API-key session has no
    /// plan limits: `rate_limits` is null or empty, and that is a valid report with no windows.
    public static ClaudeStatus Parse(string streamJson)
    {
        JsonElement? report = null;
        string? resultText = null;
        var documents = new List<JsonDocument>();
        try
        {
            foreach (var line in streamJson.Split('\n'))
            {
                if (Line(line) is not { } document) continue;
                documents.Add(document);
                var root = document.RootElement;
                if (report is null && FindReport(root) is { } found) report = found;
                if (resultText is null && JsonFields.String(root, "type") == "result") resultText = JsonFields.String(root, "result");
            }

            if (report is not { } usage)
                throw new FormatException(NoReport(resultText));
            return new ClaudeStatus(Window(usage, "session"), Window(usage, "weekly_all"), ContextPct: null);
        }
        finally
        {
            foreach (var document in documents) document.Dispose();
        }
    }

    /// The text of the stream's `result` line, or null if there is none: what `/usage` printed.
    internal static string? ResultText(string streamJson)
    {
        foreach (var line in streamJson.Split('\n'))
        {
            if (Line(line) is not { } document) continue;
            using (document)
            {
                if (JsonFields.String(document.RootElement, "type") == "result" && JsonFields.String(document.RootElement, "result") is { } text)
                    return text;
            }
        }

        return null;
    }

    /// The line as a JSON object, or null for anything else (blank, truncated, not JSON, not an object).
    static JsonDocument? Line(string line)
    {
        var text = line.Trim();
        if (text.Length == 0) return null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
        document.Dispose();
        return null;
    }

    /// `message.usage_report`, or `usage_report` at the top level of the line. Null when the line has neither.
    static JsonElement? FindReport(JsonElement root)
    {
        if (JsonFields.Object(root, "message") is { } message && Report(message) is { } inMessage) return inMessage;
        return Report(root);
    }

    static JsonElement? Report(JsonElement parent)
    {
        if (!JsonFields.Has(parent, "usage_report")) return null;
        return JsonFields.Object(parent, "usage_report") ?? throw new FormatException("usage_report is not an object");
    }

    static ClaudeWindow? Window(JsonElement report, string kind)
    {
        if (!JsonFields.Has(report, "rate_limits")) return null; // API-key sessions have no plan limits
        var limits = JsonFields.Object(report, "rate_limits")
            ?? throw new FormatException("usage_report.rate_limits is neither null nor an object");
        if (!JsonFields.Has(limits, "limits")) return null;
        if (!limits.TryGetProperty("limits", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new FormatException("usage_report.rate_limits.limits is not a list");

        foreach (var entry in list.EnumerateArray())
        {
            if (JsonFields.String(entry, "kind") != kind) continue;
            var percent = JsonFields.Number(entry, "percent")
                ?? throw new FormatException($"the '{kind}' limit has no numeric percent");
            return new ClaudeWindow(percent, JsonFields.UnixSeconds(entry, "resets_at"));
        }

        return null;
    }

    static string NoReport(string? resultText)
    {
        const string message = "claude -p /usage printed no usage_report";
        return string.IsNullOrWhiteSpace(resultText) ? message : $"{message}: {Excerpt(resultText)}";
    }

    /// The first 120 characters of the text on one line, for an error message.
    internal static string Excerpt(string text)
    {
        text = text.Trim().ReplaceLineEndings(" ");
        if (text.Length <= ResultTextLength) return text;
        // Don't cut a surrogate pair in half.
        return text[..(char.IsHighSurrogate(text[ResultTextLength - 1]) ? ResultTextLength - 1 : ResultTextLength)];
    }
}
