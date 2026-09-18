// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

namespace xFreeRamWindows;

/// <summary>
/// Process names Boost Mode must never close, and a belt-and-suspenders exclusion list
/// for services on top of the primary guard, <see cref="SafeBootServices"/> — Windows'
/// own Safe Mode with Networking essential-service list.
/// </summary>
internal static class ProtectedList
{
    public static readonly HashSet<string> ProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kernel / session-critical — killing these blue-screens or logs the user out.
        "System", "Registry", "Secure System", "Memory Compression",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",

        // Hosts many unrelated OS services in one process; we stop services individually
        // via ServiceController instead, never by killing the shared host process.
        "svchost",

        // Shell / desktop — killing these breaks the taskbar, Start menu, or text input
        // system-wide, not just the one app.
        "explorer", "dwm", "sihost", "ctfmon", "fontdrvhost",
        "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "TextInputHost", "RuntimeBroker", "ApplicationFrameHost", "dllhost",

        // Tooling that other protected components or Windows itself depend on.
        "conhost", "WmiPrvSE", "wudfhost",
    };

    public static readonly HashSet<string> ServiceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core RPC/COM plumbing — most of Windows silently depends on these.
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "Power", "PlugPlay",

        // Logging/scheduling/policy — stopping these causes cascading failures
        // in unrelated apps and services.
        "EventLog", "EventSystem", "Schedule", "Winmgmt", "SamSs", "ProfSvc", "gpsvc",

        // Networking — stopping these drops name resolution / DHCP / firewall
        // for the whole machine, not just the target app.
        "BFE", "mpssvc", "Dnscache", "Dhcp", "nsi", "NlaSvc", "Wcmsvc", "netprofm",

        // Crypto services many installers/updaters and TLS stacks assume are up.
        "CryptSvc",
    };

    public static bool IsProtectedProcess(string processName) => ProcessNames.Contains(processName);

    public static bool IsProtectedService(string serviceName) => ServiceNames.Contains(serviceName);
}
