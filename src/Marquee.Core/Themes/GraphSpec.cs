using SkiaSharp;

namespace Marquee.Core.Themes;

/// How a graph draws each series: a line (with the area under it only when the style sets a fill), a filled area under
/// a line, or one column per sample.
public enum GraphKind { Line, Area, Columns }

/// One sensor key's history on a graph. Style is layered over the graph's own, so a graph can set the line width once
/// and each series its color; StyleSpec.Empty draws it in the graph's style.
public sealed record GraphSeries(string Key, StyleSpec Style);

/// Horizontal lines at even steps of a graph's vertical scale: Lines of them split its height into Lines + 1 bands.
public sealed record GraphGrid(int Lines, SKColor Color, float Width);
