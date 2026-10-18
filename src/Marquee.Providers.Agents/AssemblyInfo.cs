using System.Runtime.InteropServices;

// Resolve native libraries (kernel32, advapi32, user32, userenv) from System32 only, never next to the exe.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
