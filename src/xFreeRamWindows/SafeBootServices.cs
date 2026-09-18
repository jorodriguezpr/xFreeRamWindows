// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using Microsoft.Win32;

namespace xFreeRamWindows;

/// <summary>
/// Defines "essential" as whatever Windows itself loads when booted into Safe Mode with
/// Networking — the OS's own, authoritative minimum-service list — rather than a
/// hand-curated guess. Boost Now keeps every service in this set (plus
/// <see cref="ProtectedList"/> as a belt-and-suspenders backstop) and only stops
/// currently-running services that are in neither.
///
/// Windows records this set at
/// HKLM\SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal and \Network — each subkey name
/// under those keys is a service (or driver) name required for that boot mode. "Network"
/// is Safe Mode with Networking; "Minimal" (plain Safe Mode) is a strict subset and is
/// included too as extra insurance.
/// </summary>
internal static class SafeBootServices
{
    private const string SafeBootKeyPath = @"SYSTEM\CurrentControlSet\Control\SafeBoot";

    /// <summary>
    /// Below this many combined names, treat the read as unreliable rather than trust an
    /// unexpectedly small essential set — see <see cref="TryGetEssentialServiceNames"/>.
    /// </summary>
    private const int MinimumPlausibleCount = 5;

    private static HashSet<string>? _cachedNames;

    /// <summary>
    /// Attempts to read the Safe Mode essential-service set from the registry. Returns
    /// false if the registry couldn't be read or returned an implausibly small result —
    /// callers must treat false as "cannot safely determine what's essential" and skip
    /// stopping any services that run, rather than defaulting to an empty (unsafe) set.
    /// </summary>
    public static bool TryGetEssentialServiceNames(out HashSet<string> names)
    {
        if (_cachedNames is not null)
        {
            names = _cachedNames;
            return true;
        }

        var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSubKeyNames(collected, $@"{SafeBootKeyPath}\Minimal");
        AddSubKeyNames(collected, $@"{SafeBootKeyPath}\Network");

        if (collected.Count < MinimumPlausibleCount)
        {
            names = collected;
            return false;
        }

        _cachedNames = collected;
        names = collected;
        return true;
    }

    private static void AddSubKeyNames(HashSet<string> names, string keyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key is null) return;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                names.Add(subKeyName);
            }
        }
        catch
        {
            // Leave whatever was already collected — the caller's plausibility check
            // catches a partial or empty result.
        }
    }
}
