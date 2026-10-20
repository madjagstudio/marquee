using System.ComponentModel;
using Marquee.App.Tests.TestSupport;
using Marquee.Host.Updates;

namespace Marquee.App.Tests;

/// The tray's side of updates: the offer's balloon and menu item, "Check for updates", and installing, with GitHub, the
/// clock, the daily timer and msiexec faked.
public sealed class TrayUpdateMenuTests : IDisposable
{
    const string ReleasesPage = "https://github.com/madjagstudios/marquee/releases";

    readonly TrayRig _rig = new();

    public void Dispose() => _rig.Dispose();

    FakeTrayEngine Engine => _rig.Engine;

    FakeHost Host => _rig.Host;

    UpdateRig Updates => _rig.Updates;

    async Task<UpdateOffer> Offered()
    {
        await _rig.Model.CheckForUpdates().WaitAsync(Wait.Timeout);
        return Updates.Updates.Offer!;
    }

    [Fact]
    public async Task Start_starts_the_daily_check_and_an_offer_shows_a_balloon_and_a_menu_item()
    {
        Assert.Equal("Check for updates", _rig.Item(MenuKeys.CheckForUpdates)?.Text);

        await _rig.Model.Start().WaitAsync(Wait.Timeout);
        Assert.True(Updates.Delay.WaitForRequests(1));
        Updates.Delay.Elapse();

        Assert.True(await Wait.UntilAsync(() => !Host.Balloons.IsEmpty));
        Assert.Equal([("Update available", "Marquee 0.2.0 is available. Click to install.", false)], Host.Balloons);
        Assert.Equal("Install Marquee 0.2.0…", _rig.Item(MenuKeys.InstallUpdate)?.Text);
        Assert.Null(_rig.Item(MenuKeys.CheckForUpdates));
    }

    [Fact]
    public async Task With_updates_off_in_config_yaml_start_leaves_the_daily_check_off_until_it_is_turned_on()
    {
        Engine.CheckForUpdates = false;

        await _rig.Model.Start().WaitAsync(Wait.Timeout);
        Assert.True(Updates.Delay.WaitForRequests(1));
        Assert.Equal(Timeout.InfiniteTimeSpan, Updates.Delay.Requested[0]);

        Engine.CheckForUpdates = true;
        Engine.RaiseConfigApplied();

        Assert.True(Updates.Delay.WaitForRequests(2));
        Assert.Equal(UpdateSchedule.FirstCheckAfterStart, Updates.Delay.Requested[1]);
    }

    [Fact]
    public async Task An_automatic_check_that_fails_is_only_logged()
    {
        Updates.GitHub.ApiFailure = new HttpRequestException("No such host is known.");
        await _rig.Model.Start().WaitAsync(Wait.Timeout);
        Assert.True(Updates.Delay.WaitForRequests(1));

        Updates.Delay.Elapse();

        Assert.True(Updates.Delay.WaitForRequests(2));
        Assert.Empty(Host.Balloons);
    }

    [Fact]
    public async Task Check_for_updates_with_nothing_new_says_this_is_the_latest()
    {
        Updates.GitHub.Latest = "0.1.0";

        _rig.Item(MenuKeys.CheckForUpdates)!.Command!();

        Assert.True(await Wait.UntilAsync(() => !Host.Balloons.IsEmpty));
        Assert.Equal([("Marquee is up to date", "You have the latest version (0.1.0).", false)], Host.Balloons);
    }

    [Fact]
    public async Task Check_for_updates_that_fails_says_why()
    {
        Updates.GitHub.ApiFailure = new HttpRequestException("No such host is known.");

        await _rig.Model.CheckForUpdates().WaitAsync(Wait.Timeout);

        var balloon = Assert.Single(Host.Balloons);
        Assert.Equal("Marquee couldn't check for updates", balloon.Title);
        Assert.Contains("No such host is known.", balloon.Text);
        Assert.True(balloon.IsError);
    }

    [Fact]
    public async Task Check_for_updates_that_finds_one_offers_it_and_its_balloon_installs_it()
    {
        await _rig.Model.CheckForUpdates().WaitAsync(Wait.Timeout);

        Assert.Equal([("Update available", "Marquee 0.2.0 is available. Click to install.", false)], Host.Balloons);
        Host.ClickBalloon();

        Assert.True(await Wait.UntilAsync(() => !Updates.Processes.Started.IsEmpty));
        Assert.Single(Updates.Processes.Started);
    }

    [Fact]
    public async Task Install_downloads_then_pauses_starts_the_installer_and_waits_paused_until_the_installer_quits_the_tray()
    {
        var offer = await Offered();
        Updates.Processes.OnStart = () => Engine.Note("msiexec");
        Host.OnExit = () => Engine.Note("Exit");

        var install = _rig.Model.InstallUpdate(offer);

        // Paused, not stopped: the screen and the ring are let go of, and the tray waits for the installer.
        Assert.True(await Wait.UntilAsync(() => !Updates.Processes.Started.IsEmpty));
        await _rig.Flush();
        Assert.Equal(["Pause", "msiexec"], Engine.CallNames);
        Assert.False(install.IsCompleted);
        Assert.Equal(0, Host.Exits);
        Assert.Equal("Installing Marquee 0.2.0…", _rig.Model.StatusLine);
        Assert.False(_rig.Item(MenuKeys.InstallUpdate)!.Enabled);
        var (fileName, arguments) = Assert.Single(Updates.Processes.Started);
        Assert.Equal(VerifiedUpdate.MsiexecPath, fileName);
        Assert.Equal(VerifiedUpdate.MsiexecArguments(Path.Combine(Updates.Downloads, "Marquee-0.2.0-x64.msi")), arguments);
        Assert.Equal([Updates.Downloads], Updates.SecuredFolders);

        // The installer asks the tray to quit before it replaces its files, and the tray quits as Quit does.
        await _rig.Model.Quit().WaitAsync(Wait.Timeout);
        await install.WaitAsync(Wait.Timeout);

        Assert.Equal(["Pause", "msiexec", "Dispose", "Exit"], Engine.CallNames);
        Assert.All(Engine.Calls.Take(3), call => Assert.NotEqual(Environment.CurrentManagedThreadId, call.Thread));
        Assert.Equal(1, Host.Exits);
        Assert.Single(Host.Balloons); // the offer's only
    }

    [Theory]
    [InlineData(1602)] // the user cancelled
    [InlineData(1603)] // it failed
    [InlineData(1618)] // another installation was under way
    public async Task An_installer_that_fails_gives_the_screen_back_and_says_where_its_log_is(int exitCode)
    {
        var offer = await Offered();
        var install = _rig.Model.InstallUpdate(offer);
        Assert.True(await Wait.UntilAsync(() => !Updates.Processes.Started.IsEmpty));

        Updates.Processes.Last.Exit(exitCode);
        await install.WaitAsync(Wait.Timeout);

        Assert.Equal(["Pause", "Resume"], Engine.CallNames);
        Assert.Equal(0, Host.Exits);
        var log = Path.Combine(Updates.Downloads, "Marquee-0.2.0-x64.msi.log");
        Assert.Equal(("Marquee couldn't install the update", $"The update didn't install (code {exitCode}). See {log}.", true),
            Host.Balloons.Last());
        Assert.True(Updates.Processes.Last.Disposed);
        Assert.True(_rig.Item(MenuKeys.InstallUpdate)!.Enabled); // to try again
        Assert.NotEqual("Installing Marquee 0.2.0…", _rig.Model.StatusLine);
    }

    [Fact]
    public async Task After_a_failed_install_the_installer_is_let_go_of_and_the_update_can_be_installed_again()
    {
        var offer = await Offered();
        var first = _rig.Model.InstallUpdate(offer);
        Assert.True(await Wait.UntilAsync(() => !Updates.Processes.Started.IsEmpty));
        Updates.Processes.Last.Exit(1618);
        await first.WaitAsync(Wait.Timeout);

        var second = _rig.Model.InstallUpdate(offer);

        Assert.True(await Wait.UntilAsync(() => Updates.Processes.Started.Count == 2));
        Assert.Equal(2, Updates.GitHub.InstallerRequests);
        Updates.Processes.Last.Exit(1603);
        await second.WaitAsync(Wait.Timeout);
        Assert.Equal("Marquee couldn't install the update", Host.Balloons.Last().Title);
    }

    [Fact]
    public async Task An_installer_that_finishes_without_quitting_the_tray_gives_the_screen_back_quietly()
    {
        // Exit code 0 with the tray still running: nothing needed doing.
        var offer = await Offered();
        var install = _rig.Model.InstallUpdate(offer);
        Assert.True(await Wait.UntilAsync(() => !Updates.Processes.Started.IsEmpty));

        Updates.Processes.Last.Exit(0);
        await install.WaitAsync(Wait.Timeout);

        Assert.Equal(["Pause", "Resume"], Engine.CallNames);
        Assert.Equal(0, Host.Exits);
        Assert.Single(Host.Balloons); // the offer's only
    }

    [Fact]
    public async Task Paused_before_it_stays_paused_when_the_installer_fails()
    {
        var offer = await Offered();
        Engine.IsPaused = true;
        var install = _rig.Model.InstallUpdate(offer);
        Assert.True(await Wait.UntilAsync(() => !Updates.Processes.Started.IsEmpty));

        Updates.Processes.Last.Exit(1603);
        await install.WaitAsync(Wait.Timeout);

        Assert.Equal(["Pause"], Engine.CallNames);
        Assert.Equal("Marquee couldn't install the update", Host.Balloons.Last().Title);
    }

    [Fact]
    public async Task While_downloading_the_status_line_shows_progress_and_the_item_waits()
    {
        var offer = await Offered();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Updates.GitHub.HoldInstallerHalfway = hold;

        var install = _rig.Model.InstallUpdate(offer);

        Assert.True(await Wait.UntilAsync(() => _rig.Model.StatusLine == "Downloading Marquee 0.2.0… 50%"));
        var item = _rig.Item(MenuKeys.InstallUpdate);
        Assert.False(item!.Enabled);
        Assert.Empty(Engine.Calls); // the engine runs on while it downloads

        hold.SetResult();
        Assert.True(await Wait.UntilAsync(() => !Updates.Processes.Started.IsEmpty));
        Updates.Processes.Last.Exit(0);
        await install.WaitAsync(Wait.Timeout);
    }

    [Fact]
    public async Task A_tampered_installer_is_refused_and_everything_carries_on()
    {
        var offer = await Offered();
        Updates.GitHub.TamperedInstaller = true;

        await _rig.Model.InstallUpdate(offer).WaitAsync(Wait.Timeout);

        Assert.Empty(Engine.Calls);
        Assert.Empty(Updates.Processes.Started);
        Assert.Equal(0, Host.Exits);
        var balloon = Host.Balloons.Last();
        Assert.Equal("Marquee couldn't install the update", balloon.Title);
        Assert.Contains("signature", balloon.Text);
        Assert.True(balloon.IsError);
        Assert.True(_rig.Item(MenuKeys.InstallUpdate)!.Enabled); // to try again
    }

    [Fact]
    public async Task An_installer_that_doesnt_start_gives_the_screen_back()
    {
        var offer = await Offered();
        Updates.Processes.Failure = new Win32Exception(2, "The system cannot find the file specified.");

        await _rig.Model.InstallUpdate(offer).WaitAsync(Wait.Timeout);

        Assert.Equal(["Pause", "Resume"], Engine.CallNames);
        Assert.Equal(0, Host.Exits);
        Assert.Equal("Marquee couldn't install the update", Host.Balloons.Last().Title);
        Assert.NotNull(_rig.Item(MenuKeys.InstallUpdate));
    }

    [Fact]
    public async Task Paused_before_it_stays_paused_when_the_installer_doesnt_start()
    {
        var offer = await Offered();
        Engine.IsPaused = true;
        Updates.Processes.Failure = new Win32Exception(2, "The system cannot find the file specified.");

        await _rig.Model.InstallUpdate(offer).WaitAsync(Wait.Timeout);

        Assert.Equal(["Pause"], Engine.CallNames);
    }

    [Fact]
    public async Task Quit_cancels_a_download_and_installs_nothing()
    {
        var offer = await Offered();
        Updates.GitHub.HoldInstallerHalfway = new TaskCompletionSource();
        var install = _rig.Model.InstallUpdate(offer);
        Assert.True(await Wait.UntilAsync(() => Updates.Updates.Downloading?.Percent == 50)); // half the installer is on disk

        await _rig.Model.Quit().WaitAsync(Wait.Timeout);
        await install.WaitAsync(Wait.Timeout);

        Assert.Equal(["Dispose"], Engine.CallNames);
        Assert.Empty(Updates.Processes.Started);
        Assert.Equal(1, Host.Exits);
        Assert.Single(Host.Balloons); // the offer's, and no error for the cancelled download
        Assert.Empty(Directory.GetFiles(Updates.Downloads, "*.msi*"));
    }

    [Fact]
    public async Task Unelevated_the_offer_opens_the_release_page_instead_of_installing()
    {
        Engine.IsElevated = false;
        await _rig.Model.CheckForUpdates().WaitAsync(Wait.Timeout);

        Assert.Equal([("Update available", "Marquee 0.2.0 is available. Click to open its download page.", false)], Host.Balloons);
        var item = _rig.Item(MenuKeys.InstallUpdate);
        Assert.Equal("Download Marquee 0.2.0…", item?.Text);

        item!.Command!();

        Assert.True(await Wait.UntilAsync(() => !_rig.Shell.Opened.IsEmpty));
        Assert.Equal([FakeGitHub.ReleasePage("0.2.0")], _rig.Shell.Opened);
        Assert.Equal(0, Updates.GitHub.InstallerRequests);
        Assert.Empty(Engine.Calls);
        Assert.Equal(0, Host.Exits);
    }

    [Fact]
    public async Task With_the_placeholder_key_check_for_updates_points_to_the_releases_page()
    {
        using var rig = new TrayRig(placeholderKey: true);

        await rig.Model.CheckForUpdates().WaitAsync(Wait.Timeout);

        Assert.Equal(
            [("This build can't install updates", $"Download new versions from {ReleasesPage}. Click here to open it.", false)],
            rig.Host.Balloons);
        Assert.Equal(0, rig.Updates.GitHub.ApiRequests);

        rig.Host.ClickBalloon();

        Assert.True(await Wait.UntilAsync(() => !rig.Shell.Opened.IsEmpty));
        Assert.Equal([ReleasesPage], rig.Shell.Opened);
    }
}
