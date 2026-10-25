namespace Marquee.App;

/// Windows waking from sleep.
interface IPowerEvents
{
    event Action? Woke;
}
