using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Marquee.Host.Updates;

/// Versions as the updater compares them. A release is tagged with a plain SemVer core version, MAJOR.MINOR.PATCH, and
/// an optional "v" (v0.2.0): no pre-release tag and no build metadata, so a release candidate is never offered as an
/// update even when it isn't marked as a prerelease on GitHub, and every offered version names one installer
/// (ReleaseFeed.InstallerName). The running Marquee's version is its AssemblyInformationalVersion, which the SDK writes
/// as the project's version and "+<commit>": the commit is dropped. A pre-release tag on the running version
/// (0.2.0-rc.1) is kept, so by SemVer's precedence that build is older than the 0.2.0 release and is offered it.
public static partial class ReleaseVersion
{
    /// Reads a release's tag: "v1.2.3" or "1.2.3", digits only, no leading zeros, nothing before or after.
    public static bool TryParseTag(string? tag, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        return tag is not null && Core(Tag().Match(tag), out version);
    }

    /// Reads the running Marquee's informational version: "1.2.3", "1.2.3-rc.1", either with "+<anything>" after it.
    public static bool TryParseRunning(string? informationalVersion, [NotNullWhen(true)] out RunningVersion? running)
    {
        running = null;
        if (informationalVersion is null) return false;

        var plus = informationalVersion.IndexOf('+');
        var match = Running().Match(plus < 0 ? informationalVersion : informationalVersion[..plus]);
        if (!Core(match, out var core)) return false;

        var preRelease = match.Groups["pre"];
        running = new RunningVersion(core, preRelease.Success ? preRelease.Value : null);
        return true;
    }

    /// The entry assembly's informational version (the tray's own, in the tray), or null when it has none.
    public static string? EntryAssembly =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    static bool Core(Match match, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (!match.Success || !Number(match, "major", out var major) || !Number(match, "minor", out var minor) || !Number(match, "patch", out var patch))
            return false;

        // Always three parts, so 0.2.0 from a tag and from the assembly compare equal (Version's unset parts are -1).
        version = new Version(major, minor, patch);
        return true;
    }

    // The pattern has checked the digits; this only refuses a number too large for Version.
    static bool Number(Match match, string group, out int value) =>
        int.TryParse(match.Groups[group].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    // [0-9], not \d, which matches every script's digits, and \z, not $, which also matches before a final line break.
    [GeneratedRegex(@"^v?(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)\z")]
    private static partial Regex Tag();

    [GeneratedRegex(@"^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z")]
    private static partial Regex Running();
}

/// The running Marquee's version: its core version and, for a pre-release build, the tag after the dash.
public sealed record RunningVersion(Version Core, string? PreRelease)
{
    /// Whether the release `release` (a core version) is newer than this: a higher version, or the same one when this is
    /// a pre-release of it.
    public bool IsOlderThan(Version release) => release > Core || (release == Core && PreRelease is not null);

    /// "1.2.3" or "1.2.3-rc.1", as the User-Agent and the log show it.
    public override string ToString() => PreRelease is null ? Core.ToString(3) : $"{Core.ToString(3)}-{PreRelease}";
}
