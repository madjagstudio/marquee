using System.Collections;
using System.Reflection;

namespace Marquee.Core.Tests.TestSupport;

/// Compares two object graphs field by field. Records compare the lists they hold by reference, so two specs from
/// separate parses are never Equal even when they say the same thing.
static class Structural
{
    public static void Equal(object? expected, object? actual) => Compare(expected, actual, "spec");

    static void Compare(object? expected, object? actual, string path)
    {
        if (expected is null || actual is null)
        {
            Assert.True(expected is null && actual is null, $"{path}: {expected ?? "null"} vs {actual ?? "null"}");
            return;
        }

        var type = expected.GetType();
        Assert.True(type == actual.GetType(), $"{path}: {type.Name} vs {actual.GetType().Name}");

        if (type.IsValueType || expected is string or Delegate)
        {
            Assert.True(expected.Equals(actual), $"{path}: {expected} vs {actual}");
            return;
        }

        if (expected is IEnumerable sequence)
        {
            var left = sequence.Cast<object?>().ToList();
            var right = ((IEnumerable)actual).Cast<object?>().ToList();
            Assert.True(left.Count == right.Count, $"{path}: {left.Count} items vs {right.Count}");
            for (var i = 0; i < left.Count; i++)
                Compare(left[i], right[i], $"{path}[{i}]");
            return;
        }

        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                Compare(field.GetValue(expected), field.GetValue(actual), $"{path}.{field.Name}");
    }
}
