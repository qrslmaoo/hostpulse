using System.Diagnostics;

namespace HostPulse;

public static class SystemSnapshot
{
    public static float GetCpuUsage()
    {
        using var cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        cpu.NextValue();
        Thread.Sleep(500);
        return cpu.NextValue();
    }

    public static float GetAvailableMemoryMB()
    {
        using var mem = new PerformanceCounter("Memory", "Available MBytes");
        return mem.NextValue();
    }
}
