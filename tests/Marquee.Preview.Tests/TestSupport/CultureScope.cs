using System.Globalization;

namespace Marquee.Preview.Tests.TestSupport;

/// Runs the test as a user of another culture (formatting and UI language) until disposed.
sealed class CultureScope : IDisposable
{
    readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string name)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
