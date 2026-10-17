namespace Marquee.Core.Rendering;

/// Eases a widget's displayed number towards each new value (cubic ease-out), for the `smooth` field.
sealed class Smoother
{
    double _from;
    double _to;
    double _current;
    TimeSpan _start;
    bool _started;

    public double Next(double target, TimeSpan now, double durationMs)
    {
        if (!_started || durationMs <= 0)
        {
            _started = true;
            _from = _to = _current = target;
            _start = now;
            return target;
        }

        if (target != _to)
        {
            _from = _current;
            _to = target;
            _start = now;
        }

        var t = Math.Clamp((now - _start).TotalMilliseconds / durationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        _current = _from + (_to - _from) * eased;
        return _current;
    }
}
