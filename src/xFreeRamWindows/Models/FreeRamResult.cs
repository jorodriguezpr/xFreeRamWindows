// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

namespace xFreeRamWindows.Models;

/// <summary>Result of a plain memory-reclaim pass — no processes or services are stopped.</summary>
internal sealed class FreeRamResult
{
    public int ProcessesTrimmed { get; set; }
    public bool StandbyListPurged { get; set; }
    public long FreedMB { get; set; }
}
