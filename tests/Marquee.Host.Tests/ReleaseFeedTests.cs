using System.Text.Json.Nodes;
using Marquee.Host.Tests.TestSupport;
using Marquee.Host.Updates;

namespace Marquee.Host.Tests;

public class ReleaseFeedTests
{
    static UpdateOffer Read(JsonObject release)
    {
        Assert.True(ReleaseFeed.TryRead(release.ToJsonString(), out var offer, out var problem), problem);
        return offer;
    }

    static string Refused(string json)
    {
        Assert.False(ReleaseFeed.TryRead(json, out var offer, out var problem));
        Assert.Null(offer);
        Assert.False(string.IsNullOrWhiteSpace(problem));
        return problem;
    }

    static string Refused(JsonObject release) => Refused(release.ToJsonString());

    [Fact]
    public void A_release_with_its_installer_and_signature_is_offered()
    {
        var offer = Read(ReleaseJson.Release("v0.2.0", installerSize: 4_321_000, signatureSize: 104));

        Assert.Equal(new Version(0, 2, 0), offer.Version);
        Assert.Equal("Marquee 0.2.0", offer.Title);
        Assert.Equal(new Uri("https://github.com/madjagstudios/marquee/releases/tag/v0.2.0"), offer.ReleasePage);
        Assert.Equal(new ReleaseAsset("Marquee-0.2.0-x64.msi", 4_321_000,
            new Uri($"{ReleaseJson.Downloads}/v0.2.0/Marquee-0.2.0-x64.msi")), offer.Installer);
        Assert.Equal(new ReleaseAsset("Marquee-0.2.0-x64.msi.sig", 104,
            new Uri($"{ReleaseJson.Downloads}/v0.2.0/Marquee-0.2.0-x64.msi.sig")), offer.Signature);
    }

    [Fact]
    public void The_file_names_are_fixed_by_the_version()
    {
        Assert.Equal("Marquee-0.2.0-x64.msi", ReleaseFeed.InstallerName(new Version(0, 2, 0)));
        Assert.Equal("Marquee-10.0.3-x64.msi.sig", ReleaseFeed.SignatureName(new Version(10, 0, 3)));
    }

    [Fact]
    public void A_release_without_a_title_or_page_is_still_offered()
    {
        var release = ReleaseJson.Release();
        release.Remove("name");
        release["html_url"] = "http://example.com/not-github";

        var offer = Read(release);

        Assert.Null(offer.Title);
        Assert.Null(offer.ReleasePage);
    }

    [Fact]
    public void A_draft_is_refused()
    {
        var release = ReleaseJson.Release();
        release["draft"] = true;

        Refused(release);
    }

    [Fact]
    public void A_prerelease_is_refused()
    {
        var release = ReleaseJson.Release();
        release["prerelease"] = true;

        Refused(release);
    }

    [Fact]
    public void A_release_without_its_installer_is_refused_naming_the_file()
    {
        Assert.Contains("Marquee-0.2.0-x64.msi", Refused(ReleaseJson.Release().Without("Marquee-0.2.0-x64.msi")));
    }

    [Fact]
    public void A_release_without_the_signature_is_refused_naming_the_file()
    {
        Assert.Contains("Marquee-0.2.0-x64.msi.sig", Refused(ReleaseJson.Release().Without("Marquee-0.2.0-x64.msi.sig")));
    }

    [Theory]
    [InlineData("Marquee-0.2.0.msi")]
    [InlineData("marquee-0.2.0-x64.msi")]
    [InlineData("Marquee-0.2.0-arm64.msi")]
    [InlineData("Marquee-0.2.1-x64.msi")]
    [InlineData("Marquee-v0.2.0-x64.msi")]
    public void An_installer_under_another_name_is_not_the_installer(string name)
    {
        var release = ReleaseJson.Release();
        release.AssetNamed("Marquee-0.2.0-x64.msi")["name"] = name;

        Refused(release);
    }

    [Theory]
    [InlineData("v0.2.0-rc.1")]
    [InlineData("v0.2")]
    [InlineData("v0.2.0.1")]
    [InlineData("latest")]
    [InlineData("")]
    public void A_tag_that_isnt_a_plain_version_is_refused(string tag)
    {
        var release = ReleaseJson.Release();
        release["tag_name"] = tag;

        Refused(release);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(300L * 1024 * 1024 + 1)]
    public void An_installer_of_no_size_or_over_300_MB_is_refused(long size)
    {
        Refused(ReleaseJson.Release(installerSize: size));
    }

    [Fact]
    public void An_installer_of_exactly_300_MB_is_offered()
    {
        Assert.Equal(300L * 1024 * 1024, Read(ReleaseJson.Release(installerSize: 300L * 1024 * 1024)).Installer.Size);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    public void A_signature_of_no_size_or_over_1_KB_is_refused(long size)
    {
        Refused(ReleaseJson.Release(signatureSize: size));
    }

    [Theory]
    [InlineData("http://github.com/madjagstudios/marquee/releases/download/v0.2.0/Marquee-0.2.0-x64.msi")]
    [InlineData("https://example.com/Marquee-0.2.0-x64.msi")]
    [InlineData("https://github.com.example.com/Marquee-0.2.0-x64.msi")]
    [InlineData("file:///C:/Marquee-0.2.0-x64.msi")]
    [InlineData("not a url")]
    public void A_download_anywhere_but_GitHub_over_https_is_refused(string url)
    {
        var release = ReleaseJson.Release();
        release.AssetNamed("Marquee-0.2.0-x64.msi")["browser_download_url"] = url;

        Refused(release);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"tag_name": 5}""")]
    [InlineData("""{"tag_name": "v0.2.0", "draft": false, "prerelease": false, "assets": {}}""")]
    [InlineData("""{"tag_name": "v0.2.0", "draft": "no", "prerelease": false, "assets": []}""")]
    [InlineData("""{"tag_name": "v0.2.0", "assets": [null, 3, "x", {"name": 1}]}""")]
    public void Malformed_JSON_is_refused_without_throwing(string json)
    {
        Refused(json);
    }

    [Fact]
    public void An_asset_whose_size_isnt_a_number_is_refused()
    {
        var release = ReleaseJson.Release();
        release.AssetNamed("Marquee-0.2.0-x64.msi")["size"] = "4321000";

        Refused(release);
    }
}
