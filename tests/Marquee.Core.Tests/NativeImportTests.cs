using System.Reflection;
using System.Runtime.InteropServices;
using Marquee.Core.Video;

namespace Marquee.Core.Tests;

/// Native libraries load from System32 only, never from the exe's folder, where a portable copy unpacked into Downloads
/// could find a planted DLL of the same name. Core's assembly attribute covers every platform call in it (USB, jobs,
/// timers, Media Foundation); this keeps a new call, or a method-level attribute, from quietly widening the search.
public class NativeImportTests
{
    [Fact]
    public void Every_platform_call_in_core_resolves_from_system32_only()
    {
        var assembly = typeof(MediaFoundation).Assembly;
        var assemblyPaths = assembly.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()?.Paths;
        var calls = assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<DllImportAttribute>() is not null)
            .ToList();

        Assert.Contains(calls, m => m.GetCustomAttribute<DllImportAttribute>()!.Value == "mfplat.dll");
        Assert.Contains(calls, m => m.GetCustomAttribute<DllImportAttribute>()!.Value == "mfreadwrite.dll");
        Assert.All(calls, m => Assert.Equal(DllImportSearchPath.System32,
            m.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()?.Paths ?? assemblyPaths));
    }
}
