// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

using System.Management;

namespace xFreeRamWindows;

/// <summary>
/// Detects whether Virtualization-Based Security (VBS/HVCI) is active. Two real
/// bugchecks (0x18B SECURE_KERNEL_ERROR / STATUS_CONFLICTING_ADDRESSES) were caused
/// by <see cref="SystemMemoryManager.TryPurgeStandbyList"/> on a machine running
/// VBS+HVCI, so that call is gated on this returning false. Fails closed: any
/// uncertainty (WMI error, unreadable status) is treated as "VBS may be active".
/// </summary>
internal static class VbsDetector
{
    public static bool IsSecureKernelActive()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\DeviceGuard",
                "SELECT VirtualizationBasedSecurityStatus FROM Win32_DeviceGuard");

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    // 0 = off/not configured, 1 = enabled but not running, 2 = running.
                    return Convert.ToInt32(obj["VirtualizationBasedSecurityStatus"]) == 2;
                }
            }

            return true;
        }
        catch
        {
            return true;
        }
    }
}
