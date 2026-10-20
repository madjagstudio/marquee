namespace Marquee.Host.Updates;

/// What an update check found (UpdateChecker.FindAsync): a newer release (Offer); this Marquee being the latest (no
/// Offer and no Problem); or no answer it could use, with the reason, in the words it was logged with (Problem).
/// Running is this Marquee's version as the log gives it, for "you have the latest version".
public sealed record UpdateCheckResult(UpdateOffer? Offer, string? Problem, string Running)
{
    public bool IsUpToDate => Offer is null && Problem is null;
}
