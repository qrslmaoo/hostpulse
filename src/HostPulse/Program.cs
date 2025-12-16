using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace HostPulse
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("hostpulse\n");

            var cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            var mem = new PerformanceCounter("Memory", "Available MBytes");

            // First call is junk, wait briefly
            cpu.NextValue();
            Thread.Sleep(500);

            Console.WriteLine($"CPU Usage: {cpu.NextValue():0.0}%");
            Console.WriteLine($"Available Memory: {mem.NextValue():0} MB\n");

            Console.WriteLine("Top Processes by Memory:");

            var processes = Process.GetProcesses()
                .OrderByDescending(p =>
                {
                    try { return p.WorkingSet64; }
                    catch { return 0; }
                })
                .Take(5);

            foreach (var p in processes)
            {
                try
                {
                    Console.WriteLine(
                        $"{p.ProcessName,-25} {p.WorkingSet64 / (1024 * 1024)} MB");
                }
                catch { }
            }

            Console.WriteLine("\nDone.");
        }
    }
}
