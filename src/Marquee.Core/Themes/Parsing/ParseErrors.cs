namespace Marquee.Core.Themes.Parsing;

/// Where the layout parser's errors go. Throwing lets the first ThemeException leave the parse untouched, as
/// `marquee run` wants. Collecting records each one and lets the parser carry on at the next section, style, layer or
/// widget, so `marquee validate` can show every independent problem in one run. Both run the same parsing code, so a
/// file's first error is the same either way.
sealed class ParseErrors
{
    /// More than this many errors are counted but not kept: past a point a list of problems stops helping.
    public const int MaxErrors = 200;

    public static readonly ParseErrors Throwing = new(collecting: false);

    readonly List<ThemeException>? _errors;
    readonly HashSet<(int Line, string Reason)> _seen = [];
    int _omitted;

    ParseErrors(bool collecting) => _errors = collecting ? [] : null;

    public static ParseErrors Collecting() => new(collecting: true);

    public bool Collects => _errors is not null;

    /// For exception filters at the points the parser can recover from: `catch (ThemeException ex) when (errors.Absorb(ex))`.
    /// When throwing it returns false, so the exception carries on as if never caught.
    public bool Absorb(ThemeException error)
    {
        if (_errors is null) return false;
        // An aliased node is parsed once per alias, and its mistake is the same mistake each time.
        if (!_seen.Add((error.Line, error.Reason))) return true;
        if (_errors.Count < MaxErrors)
            _errors.Add(error);
        else
            _omitted++;
        return true;
    }

    /// A problem found without an exception in flight: thrown when throwing, recorded when collecting.
    public void Report(ThemeException error)
    {
        if (!Absorb(error)) throw error;
    }

    /// Parses with `parse`, or returns null if it fails and the error was collected.
    public T? Recover<T>(Func<T> parse) where T : class
    {
        try
        {
            return parse();
        }
        catch (ThemeException ex) when (Absorb(ex))
        {
            return null;
        }
    }

    /// By line, ties in the order found, then one more saying how many were left out. Errors are found section by
    /// section in the order Parse checks them, which isn't always the order they appear in the file.
    public IEnumerable<ThemeException> InDocumentOrder()
    {
        foreach (var error in (_errors ?? []).OrderBy(e => e.Line))
            yield return error;
        if (_omitted > 0)
            yield return new ThemeException($"…and {_omitted} more {(_omitted == 1 ? "error" : "errors")}");
    }
}
