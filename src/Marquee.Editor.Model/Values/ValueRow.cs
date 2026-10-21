using Marquee.Core.Sensors;

namespace Marquee.Editor.Model.Values;

/// One line of the test values panel: a key the theme reads, where its value comes from, the value (null when it is
/// missing; Unix seconds for `now`), whether it is stale, its pin (null for none, and for `now`, whose pin is
/// TestValues.PinnedNow), its slider range, and whether a graph draws it, so it can take a pattern.
public sealed record ValueRow(string Key, ValueSourceKind Source, SensorValue? Value, bool Stale, TestPin? Pin, SliderRange Range,
    bool IsGraph);
