namespace Marquee.Core.Devices;

/// The words for each orientation, as `--orientation` and config.yaml spell them, kept in one list so both accept the
/// same names.
public static class OrientationNames
{
    /// Every orientation with its name, in the order help text and error messages list them.
    public static IReadOnlyList<(string Name, Orientation Value)> All { get; } =
    [
        ("portrait", Orientation.Portrait),
        ("portrait-flipped", Orientation.PortraitFlipped),
        ("landscape", Orientation.Landscape),
        ("landscape-flipped", Orientation.LandscapeFlipped),
    ];

    /// Names are matched exactly, lower case, as they are written in the usage text.
    public static bool TryParse(string? name, out Orientation orientation)
    {
        foreach (var (candidate, value) in All)
        {
            if (candidate == name)
            {
                orientation = value;
                return true;
            }
        }

        orientation = default;
        return false;
    }

    public static string Name(this Orientation orientation)
    {
        foreach (var (name, value) in All)
            if (value == orientation) return name;
        throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Not a known orientation");
    }
}
