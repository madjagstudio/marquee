using Marquee.Core.Expressions;

namespace Marquee.Core.Themes;

/// A text widget written with `spans:` instead of `text:`: runs of text that follow each other on shared baselines, each
/// with its own template and style, such as a big number with a small unit. It is a text widget in layout.yaml, with the
/// same keys and rules; it is a record of its own so each form keeps exactly what it draws.
public sealed record SpansWidget(IReadOnlyList<TextSpan> Spans) : WidgetSpec;

/// One run of a SpansWidget. Its style holds only text keys: it goes over the widget's style, and the widget's `when`
/// rules go over both, so a rule changes every span.
public sealed record TextSpan(Template Text, StyleSpec Style);
