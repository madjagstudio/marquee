using System.Text.Json.Nodes;

namespace Marquee.Host.Tests.TestSupport;

/// GitHub's "latest release" JSON, cut down to the fields the updater reads plus a few it doesn't, for a release with an
/// installer, its signature and the CLI zip. Tests change what they need through the JsonObject.
static class ReleaseJson
{
    public const string Downloads = "https://github.com/madjagstudio/marquee/releases/download";

    public static JsonObject Release(string tag = "v0.2.0", long installerSize = 4_321_000, long signatureSize = 104)
    {
        var version = tag.TrimStart('v');
        return new JsonObject
        {
            ["url"] = "https://api.github.com/repos/madjagstudio/marquee/releases/1",
            ["html_url"] = $"https://github.com/madjagstudio/marquee/releases/tag/{tag}",
            ["id"] = 1,
            ["tag_name"] = tag,
            ["name"] = $"Marquee {version}",
            ["draft"] = false,
            ["prerelease"] = false,
            ["published_at"] = "2026-10-19T12:00:00Z",
            ["body"] = "What changed.",
            ["assets"] = new JsonArray(
                Asset($"Marquee-{version}-x64.msi", installerSize, tag),
                Asset($"Marquee-{version}-x64.msi.sig", signatureSize, tag),
                Asset($"marquee-{version}.zip", 2_000_000, tag),
                Asset($"marquee-{version}.zip.sig", 104, tag)),
        };
    }

    public static JsonObject Asset(string name, long size, string tag = "v0.2.0") => new()
    {
        ["name"] = name,
        ["size"] = size,
        ["content_type"] = "application/octet-stream",
        ["browser_download_url"] = $"{Downloads}/{tag}/{name}",
    };

    /// The release with the named asset taken out.
    public static JsonObject Without(this JsonObject release, string assetName)
    {
        var assets = release["assets"]!.AsArray();
        assets.Remove(assets.Single(asset => (string?)asset!["name"] == assetName));
        return release;
    }

    /// The named asset, to change a field of.
    public static JsonObject AssetNamed(this JsonObject release, string assetName) =>
        release["assets"]!.AsArray().Single(asset => (string?)asset!["name"] == assetName)!.AsObject();
}
