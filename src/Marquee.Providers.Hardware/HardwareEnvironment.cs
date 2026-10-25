using System.Security.Principal;
using Microsoft.Win32;

namespace Marquee.Providers.Hardware;

/// What stops LibreHardwareMonitor from reading CPU temperatures and motherboard sensors. GPU, memory and load
/// readings work without either. Users install PawnIO themselves; see THIRD-PARTY-NOTICES.md.
public static class HardwareEnvironment
{
    public const string PawnIoUrl = "https://pawnio.eu/";

    public static IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!IsElevated())
            problems.Add("Marquee is not running as administrator, so CPU temperatures and motherboard sensors are unavailable. " +
                "Run it from an elevated terminal.");
        if (!IsPawnIoInstalled())
            problems.Add($"The PawnIO driver is not installed, so CPU temperatures and motherboard sensors are unavailable. " +
                $"Install it from {PawnIoUrl}, then restart Marquee.");
        return problems;
    }

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// PawnIO's installer registers a kernel driver service named PawnIO.
    public static bool IsPawnIoInstalled()
    {
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return service is not null;
    }
}
