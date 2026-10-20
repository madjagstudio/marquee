namespace Marquee.Editor.Model.Values;

/// The ends of a key's slider in the test values panel.
public readonly record struct SliderRange(double Min, double Max)
{
    /// For a key no gauge or bar reads: most readings are percentages.
    public static SliderRange Default { get; } = new(0, 100);

    /// Both ranges at once: the lower min and the higher max.
    public SliderRange Union(SliderRange other) => new(Math.Min(Min, other.Min), Math.Max(Max, other.Max));
}
