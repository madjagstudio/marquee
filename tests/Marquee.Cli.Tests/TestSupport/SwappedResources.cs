using System.Globalization;
using System.Resources;

namespace Marquee.Cli.Tests.TestSupport;

/// Puts a stand-in ResourceManager behind Strings until disposed. Pseudo wraps every English message as [!…!], the way
/// a translation replaces it; Empty has no messages at all.
sealed class SwappedResources : IDisposable
{
    readonly ResourceManager _original = Strings.ResourceManager;

    SwappedResources(Func<ResourceManager, string, string?> lookup)
    {
        Strings.ResourceManager = new Stub((name, culture) =>
        {
            Cultures.Add(culture);
            return lookup(_original, name);
        });
    }

    /// The culture of every lookup, in order.
    public List<CultureInfo?> Cultures { get; } = [];

    public static SwappedResources Pseudo() =>
        new((english, name) => english.GetString(name, CultureInfo.InvariantCulture) is { } text ? $"[!{text}!]" : null);

    public static SwappedResources Empty() => new((_, _) => null);

    public void Dispose() => Strings.ResourceManager = _original;

    sealed class Stub(Func<string, CultureInfo?, string?> lookup) : ResourceManager
    {
        public override string? GetString(string name, CultureInfo? culture) => lookup(name, culture);
    }
}
