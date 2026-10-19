using Marquee.Core.Config;

namespace Marquee.Core.Devices;

/// What to do about a busy screen.
public enum HandoverAction
{
    /// Leave it to the busy hints.
    None,

    /// Show the tray prompt, which offers to take over.
    Prompt,

    /// Run the handover.
    TakeOver,
}

/// Decides whether a busy screen is worth a handover.
public static class HandoverPolicy
{
    /// `userAsked` is the tray's "Take over" item, which works whatever the mode says. Only L-Connect's service can be
    /// handed over from: when only its app runs, or nothing of L-Connect's does, the existing busy hints apply. Ask
    /// prompts every time it's asked; showing the prompt once per busy episode is the caller's job.
    public static HandoverAction Decide(HandoverMode mode, LConnectState state, bool userAsked)
    {
        if (state != LConnectState.Service) return HandoverAction.None;
        if (userAsked) return HandoverAction.TakeOver;
        return mode switch
        {
            HandoverMode.Auto => HandoverAction.TakeOver,
            HandoverMode.Ask => HandoverAction.Prompt,
            _ => HandoverAction.None,
        };
    }
}
