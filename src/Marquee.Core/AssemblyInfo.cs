using System.Runtime.InteropServices;

// Resolve native libraries (winusb, setupapi, kernel32) from System32 only, never next to the exe.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
