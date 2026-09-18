// Developer: Jose Rodriguez Arroyo
// Email: jrpcone@gmail.com
// GitHub: https://github.com/jorodriguezpr

namespace xFreeRamWindows.Models;

/// <summary>
/// What Boost Mode changed, so Restore can undo it. Closed apps are not relaunched by
/// design — only the services Boost stopped are recorded and restarted.
/// </summary>
internal sealed class BoostSnapshot
{
    public required string TargetProcessName { get; init; }
    public List<string> StoppedServiceNames { get; init; } = new();
    public DateTime TakenAtUtc { get; init; } = DateTime.UtcNow;
}

internal sealed class BoostResult
{
    public int ProcessesStopped { get; set; }
    public int ServicesStopped { get; set; }
    public List<string> Errors { get; } = new();
}

internal sealed class RestoreResult
{
    public int ServicesRestarted { get; set; }
    public List<string> Errors { get; } = new();
}
