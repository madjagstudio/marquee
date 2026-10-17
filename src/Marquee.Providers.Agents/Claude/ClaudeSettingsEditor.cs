using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace Marquee.Providers.Agents.Claude;

/// Something setup-claude won't do, with the reason in words for the user. Nothing was changed.
public sealed class ClaudeSetupException(string message) : Exception(message);

/// What setup-claude did (or, for a dry run, would do). NewSettings is the settings.json text it wrote or would write.
public sealed record SettingsChange(bool Changed, string Message, string? NewSettings);

/// Points Claude Code's `statusLine` at `marquee statusline` in ~/.claude/settings.json, and back. Only `statusLine` is
/// touched: every other key keeps its value and order. Each write keeps a timestamped backup of the old file and is
/// atomic. Files Marquee can't round-trip exactly (comments, trailing commas, not an object) are refused, not rewritten.
public static class ClaudeSettingsEditor
{
    static readonly JsonDocumentOptions Strict = new() { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };

    /// The status-line command for this marquee.exe, quoted for the shell Claude Code will run it in (see ClaudeShell).
    /// Git Bash takes forward slashes; PowerShell needs the call operator to run a quoted path.
    public static string BridgeCommand(string exePath, bool gitBash) => gitBash
        ? $"\"{exePath.Replace('\\', '/').Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`")}\" statusline"
        : $"& '{DoubleSingleQuotes(exePath)}' statusline";

    /// PowerShell treats the curly and low/reversed single quotes as single quotes too; each is doubled as itself.
    static string DoubleSingleQuotes(string text) =>
        text.Replace("'", "''").Replace("‘", "‘‘").Replace("’", "’’")
            .Replace("‚", "‚‚").Replace("‛", "‛‛");

    /// The program run by the bridge command is named marquee (or marquee.exe), optionally quoted, then `statusline`.
    static readonly Regex BridgeEnding = new("""marquee(\.exe)?["']?\s+statusline\s*$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// True for a statusLine that runs `marquee statusline` (from any install location). A script that merely has
    /// marquee in its path (`bash ~/marquee/run.sh statusline`) is the user's own and isn't taken for the bridge.
    public static bool IsBridge(JsonNode? statusLine) =>
        SavedStatusLine.Command(statusLine as JsonObject) is { } command && BridgeEnding.IsMatch(command);

    public static SettingsChange Install(string settingsPath, string savedPath, string bridgeCommand, DateTimeOffset now, bool dryRun = false)
    {
        var (root, original, crlf) = Load(settingsPath);
        var current = root["statusLine"];

        if (IsBridge(current))
        {
            if (SavedStatusLine.Command((JsonObject)current!) == bridgeCommand)
                return new SettingsChange(false, "Claude Code's status line already goes through Marquee. Nothing to change.", null);

            // Set up before from another install location: point it here, and keep the status line saved back then.
            current!["command"] = bridgeCommand;
            return Finish(settingsPath, root, original, crlf, now, dryRun, "Claude Code's status line now runs this copy of Marquee.");
        }

        if (current is not null and not JsonObject)
            throw new ClaudeSetupException($"statusLine in {settingsPath} is not an object, so Marquee can't tell what it does. Nothing was changed.");

        var previous = (JsonObject?)current;
        var statusLine = (JsonObject?)previous?.DeepClone() ?? [];
        statusLine["type"] = "command";
        statusLine["command"] = bridgeCommand;
        root["statusLine"] = statusLine;

        var message = SavedStatusLine.Command(previous) is { } before
            ? $"Claude Code's status line now goes through Marquee. It still shows what it showed before: marquee statusline runs `{before}` and prints its output."
            : "Claude Code's status line now goes through Marquee. There was no status line before, so it stays blank.";
        if (!dryRun) SavedStatusLine.Save(savedPath, previous);
        return Finish(settingsPath, root, original, crlf, now, dryRun, message);
    }

    public static SettingsChange Undo(string settingsPath, string savedPath, DateTimeOffset now, bool dryRun = false)
    {
        if (!File.Exists(savedPath))
            return new SettingsChange(false, "Nothing to undo: marquee setup-claude hasn't changed Claude Code's settings.", null);

        var saved = SavedStatusLine.LoadStrict(savedPath);
        var (root, original, crlf) = Load(settingsPath);
        if (!IsBridge(root["statusLine"]))
            throw new ClaudeSetupException($"statusLine in {settingsPath} no longer runs marquee statusline, so it was changed since setup; " +
                $"leaving it alone. The status line from before setup is saved in {savedPath}.");

        if (saved is null) root.Remove("statusLine");
        else root["statusLine"] = saved.DeepClone();

        var change = Finish(settingsPath, root, original, crlf, now, dryRun,
            saved is null ? "Removed Marquee's status line; Claude Code has no status line again." : "Restored Claude Code's previous status line.");
        if (!dryRun) File.Delete(savedPath);
        return change;
    }

    /// Strict: only no BOM or the UTF-8 BOM, and bytes that are valid UTF-8, so decoding can never change a value.
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static (JsonObject Root, byte[]? Original, bool Crlf) Load(string path)
    {
        if (!File.Exists(path)) return ([], null, false);

        if (new FileInfo(path).LinkTarget is not null)
            throw new ClaudeSetupException($"{path} is a link; Marquee won't replace it. Nothing was changed.");

        var original = File.ReadAllBytes(path);
        if (original.AsSpan().StartsWith<byte>([0xFF, 0xFE]) || original.AsSpan().StartsWith<byte>([0xFE, 0xFF]))
            throw new ClaudeSetupException($"{path} isn't UTF-8 text (it starts with a UTF-16 or UTF-32 byte order mark). Nothing was changed; " +
                "save it as UTF-8 and run the command again.");

        string text;
        try
        {
            var skip = original.AsSpan().StartsWith<byte>([0xEF, 0xBB, 0xBF]) ? 3 : 0;
            text = StrictUtf8.GetString(original, skip, original.Length - skip);
        }
        catch (DecoderFallbackException)
        {
            throw new ClaudeSetupException($"{path} isn't UTF-8 text (it has bytes that aren't valid UTF-8), so Marquee can't rewrite it exactly. " +
                "Nothing was changed; save it as UTF-8 and run the command again.");
        }

        try
        {
            if (JsonNode.Parse(text, documentOptions: Strict) is not JsonObject root)
                throw new ClaudeSetupException($"{path} is not a JSON object. Nothing was changed.");

            // Parsing is lazy: duplicate keys and unwritable text (a lone surrogate) only fail once the object is
            // materialized or written. Find out now, before anything is saved or replaced.
            _ = root.Count;
            _ = root.ToJsonString(SavedStatusLine.Format);
            return (root, original, text.Contains("\r\n", StringComparison.Ordinal));
        }
        catch (JsonException ex)
        {
            throw new ClaudeSetupException($"{path} isn't plain JSON that Marquee can rewrite exactly ({ex.Message}). Nothing was changed; " +
                "add the statusLine setting by hand, or fix the file and run the command again.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new ClaudeSetupException($"{path} has something Marquee can't rewrite exactly, such as a repeated key or an invalid character ({ex.Message}). " +
                "Nothing was changed; add the statusLine setting by hand, or fix the file and run the command again.");
        }
    }

    static SettingsChange Finish(string path, JsonObject root, byte[]? original, bool crlf, DateTimeOffset now, bool dryRun, string message)
    {
        var text = Serialize(root, crlf);
        if (dryRun) return new SettingsChange(true, message, text);

        if (original is null)
        {
            // Nothing was there when we looked: don't overwrite a settings.json created since.
            AtomicFile.WriteAllText(path, text, overwrite: false);
            return new SettingsChange(true, $"{message} Created {path}.", text);
        }

        if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(original))
            throw new ClaudeSetupException($"{path} changed while Marquee was editing it. Nothing was changed; run the command again.");

        var backup = $"{path}.marquee-backup-{now.ToLocalTime():yyyyMMdd-HHmmss}";
        for (var n = 2; File.Exists(backup); n++)
            backup = $"{path}.marquee-backup-{now.ToLocalTime():yyyyMMdd-HHmmss}-{n}";
        AtomicFile.Replace(path, text, backup);
        return new SettingsChange(true, $"{message} The old settings are in {backup}.", text);
    }

    /// Two-space indents; line endings follow the original file (LF for a new one), with a final newline.
    static string Serialize(JsonObject root, bool crlf)
    {
        var text = root.ToJsonString(SavedStatusLine.Format).ReplaceLineEndings("\n") + "\n";
        return crlf ? text.ReplaceLineEndings("\r\n") : text;
    }
}
