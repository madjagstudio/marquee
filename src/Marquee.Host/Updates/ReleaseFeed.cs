using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Marquee.Host.Resources;

namespace Marquee.Host.Updates;

/// Reads GitHub's answer to "the latest release of madjagstudio/marquee" (the REST API's release object). A release is
/// offered only when it is published (not a draft or a prerelease), its tag is a plain version (ReleaseVersion), and it
/// has both files the updater needs, under the fixed names the release workflow gives them:
///
///     Marquee-<version>-x64.msi        the installer, e.g. Marquee-0.2.0-x64.msi for the tag v0.2.0
///     Marquee-<version>-x64.msi.sig    its signature (ReleaseSignature)
///
/// Other files (the CLI zip and its signature) are ignored. Each of the two must say how big it is, within limits
/// (MaxInstallerBytes, MaxSignatureBytes), and be served by github.com over https, so the updater never fetches from
/// anywhere the release can point it. Anything else refuses the release with the reason, never an exception: the JSON
/// comes from the network.
public static class ReleaseFeed
{
    /// The one address the update check asks.
    public const string LatestReleaseUrl = "https://api.github.com/repos/madjagstudio/marquee/releases/latest";

    /// Marquee's releases on GitHub, for a person to download from in a browser.
    public const string ReleasesPage = "https://github.com/madjagstudio/marquee/releases";

    /// The largest installer the updater downloads, far more than Marquee needs, so a release that says more is a
    /// mistake or worse.
    public const long MaxInstallerBytes = 300L * 1024 * 1024;

    /// A .sig is about 104 bytes.
    public const int MaxSignatureBytes = 1024;

    public static string InstallerName(Version version) => $"Marquee-{version.ToString(3)}-x64.msi";

    public static string SignatureName(Version version) => InstallerName(version) + ".sig";

    /// Reads the release in `json`. True with the offer (whatever its version: the checker compares), or false with what
    /// is wrong with it, in words for the log.
    public static bool TryRead(string json, [NotNullWhen(true)] out UpdateOffer? release, [NotNullWhen(false)] out string? problem)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            problem = Read(document.RootElement, out release);
        }
        catch (JsonException ex)
        {
            release = null;
            problem = Strings.ReleaseNotJson(ex.Message);
        }

        return release is not null;
    }

    /// The problem, or null with the release.
    static string? Read(JsonElement root, out UpdateOffer? release)
    {
        release = null;
        if (root.ValueKind != JsonValueKind.Object
            || String(root, "tag_name") is not { } tag
            || Bool(root, "draft") is not { } draft
            || Bool(root, "prerelease") is not { } prerelease
            || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return Strings.ReleaseUnreadable;

        if (draft) return Strings.ReleaseIsDraft(tag);
        if (prerelease) return Strings.ReleaseIsPrerelease(tag);
        if (!ReleaseVersion.TryParseTag(tag, out var version)) return Strings.ReleaseTagNotAVersion(tag);

        if (Asset(assets, tag, InstallerName(version), MaxInstallerBytes, out var installer) is { } installerProblem)
            return installerProblem;
        if (Asset(assets, tag, SignatureName(version), MaxSignatureBytes, out var signature) is { } signatureProblem)
            return signatureProblem;

        var title = String(root, "name") is { } name && !string.IsNullOrWhiteSpace(name) ? name : null;
        var page = String(root, "html_url") is { } html && IsGitHub(html, out var uri) ? uri : null;
        release = new UpdateOffer(version, title, page, installer!, signature!);
        return null;
    }

    /// The asset named exactly `name` (GitHub keeps asset names unique within a release): the problem, or null with it.
    static string? Asset(JsonElement assets, string tag, string name, long maxSize, out ReleaseAsset? asset)
    {
        asset = null;
        foreach (var entry in assets.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !string.Equals(String(entry, "name"), name, StringComparison.Ordinal))
                continue;

            if (!entry.TryGetProperty("size", out var sizeElement) || sizeElement.ValueKind != JsonValueKind.Number
                || !sizeElement.TryGetInt64(out var size))
                return Strings.ReleaseFileUnreadable(name);
            if (size <= 0 || size > maxSize) return Strings.ReleaseFileSize(name, size, maxSize);
            if (String(entry, "browser_download_url") is not { } url || !IsGitHub(url, out var downloadUrl))
                return Strings.ReleaseFileNotOnGitHub(name);

            asset = new ReleaseAsset(name, size, downloadUrl);
            return null;
        }

        return Strings.ReleaseFileMissing(tag, name);
    }

    /// An https address on github.com itself, its default port and no user name. GitHub's download links redirect to
    /// its file host, which the client follows (never to plain http).
    static bool IsGitHub(string text, [NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(text, UriKind.Absolute, out uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && uri.UserInfo.Length == 0;

    static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
