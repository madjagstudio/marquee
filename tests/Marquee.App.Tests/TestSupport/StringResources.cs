using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;

namespace Marquee.App.Tests.TestSupport;

/// Reflection over a Strings class, whose messages are its public static properties (plain text) and methods (text
/// with placeholders). The English is read from the compiled resources, so the tests check what ships, under the
/// manifest name Strings looks up, rather than the .resx source.
static class StringResources
{
    static readonly Regex Placeholder = new(@"\{(\d+)(?:,[^:}]*)?(?::[^}]*)?\}", RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, string> English(ResourceManager resources)
    {
        var set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"{resources.BaseName} has no English resources.");
        return set.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    public static IReadOnlyList<PropertyInfo> Properties(Type strings) =>
        strings.GetProperties(BindingFlags.Public | BindingFlags.Static);

    public static IReadOnlyList<MethodInfo> Methods(Type strings) =>
        strings.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToList();

    public static IEnumerable<string> MessageNames(Type strings) =>
        Properties(strings).Select(property => property.Name).Concat(Methods(strings).Select(method => method.Name));

    /// A stand-in for each parameter: "x" for text, zero for numbers.
    public static object?[] DummyArguments(MethodInfo method) =>
        method.GetParameters()
            .Select(parameter => parameter.ParameterType == typeof(string) ? "x" : Activator.CreateInstance(parameter.ParameterType))
            .ToArray();

    /// How many different {0}-style placeholders the text has; {{ and }} are literal braces.
    public static int PlaceholderCount(string text) =>
        Placeholder.Matches(text.Replace("{{", "").Replace("}}", ""))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .Count();
}
