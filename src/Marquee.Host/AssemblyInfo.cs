using System.Runtime.InteropServices;

// Resolve native libraries (wtsapi32, kernel32, advapi32) from System32 only, never next to the exe, as Marquee.Core does.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
