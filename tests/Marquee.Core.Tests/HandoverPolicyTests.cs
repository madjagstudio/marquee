using Marquee.Core.Config;
using Marquee.Core.Devices;

namespace Marquee.Core.Tests;

public class HandoverPolicyTests
{
    [Theory]
    // L-Connect's service holds the screen: the mode decides, and the tray's "Take over" always takes it.
    [InlineData(HandoverMode.Off, LConnectState.Service, false, HandoverAction.None)]
    [InlineData(HandoverMode.Ask, LConnectState.Service, false, HandoverAction.Prompt)]
    [InlineData(HandoverMode.Auto, LConnectState.Service, false, HandoverAction.TakeOver)]
    [InlineData(HandoverMode.Off, LConnectState.Service, true, HandoverAction.TakeOver)]
    [InlineData(HandoverMode.Ask, LConnectState.Service, true, HandoverAction.TakeOver)]
    [InlineData(HandoverMode.Auto, LConnectState.Service, true, HandoverAction.TakeOver)]
    // Only the app, or nothing of L-Connect's: there's no service to stop, so the busy hints are all there is.
    [InlineData(HandoverMode.Off, LConnectState.AppOnly, false, HandoverAction.None)]
    [InlineData(HandoverMode.Ask, LConnectState.AppOnly, false, HandoverAction.None)]
    [InlineData(HandoverMode.Auto, LConnectState.AppOnly, false, HandoverAction.None)]
    [InlineData(HandoverMode.Off, LConnectState.AppOnly, true, HandoverAction.None)]
    [InlineData(HandoverMode.Ask, LConnectState.AppOnly, true, HandoverAction.None)]
    [InlineData(HandoverMode.Auto, LConnectState.AppOnly, true, HandoverAction.None)]
    [InlineData(HandoverMode.Off, LConnectState.NotRunning, false, HandoverAction.None)]
    [InlineData(HandoverMode.Ask, LConnectState.NotRunning, false, HandoverAction.None)]
    [InlineData(HandoverMode.Auto, LConnectState.NotRunning, false, HandoverAction.None)]
    [InlineData(HandoverMode.Off, LConnectState.NotRunning, true, HandoverAction.None)]
    [InlineData(HandoverMode.Ask, LConnectState.NotRunning, true, HandoverAction.None)]
    [InlineData(HandoverMode.Auto, LConnectState.NotRunning, true, HandoverAction.None)]
    public void Decide(HandoverMode mode, LConnectState state, bool userAsked, HandoverAction expected) =>
        Assert.Equal(expected, HandoverPolicy.Decide(mode, state, userAsked));
}
