using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HostPulse.UI
{
    public class MainForm : Form
    {
        private readonly List<IDisposable> disposableCounters = new();

        private readonly PerformanceCounter cpuTotal;
        private readonly PerformanceCounter ramAvailable;
        private readonly PerformanceCounter diskRead;
        private readonly PerformanceCounter diskWrite;
        private readonly PerformanceCounter netRecv;
        private readonly PerformanceCounter netSend;

        private float cpuUsage;
        private float ramUsedMB;
        private readonly float ramTotalMB;

        private float diskMBs;
        private float netKBs;

        private readonly Queue<float> cpuHistory = new();
        private readonly Queue<float> ramHistory = new();
        private readonly Queue<float> diskHistory = new();
        private readonly Queue<float> netHistory = new();

        private const int HistorySize = 120;
        private const int ProcessHistorySize = 20;

        private int tickCount = 0;
        private const int ProcessCheckInterval = 5;
        private volatile bool isDetectingSpikes = false;

        private readonly Dictionary<int, (float AvgCpu, DateTime LastChecked)> processBaselines = new();
        private readonly List<ProcessSpike> spikes = new();
        private readonly object spikeLock = new object();

        public MainForm()
        {
            Text = "HostPulse";
            Size = new Size(540, 540);
            BackColor = Color.Black;
            DoubleBuffered = true;

            cpuTotal = Track(new PerformanceCounter("Processor", "% Processor Time", "_Total"));
            ramAvailable = Track(new PerformanceCounter("Memory", "Available MBytes"));
            diskRead = Track(new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total"));
            diskWrite = Track(new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total"));

            string networkInterface = GetNetworkInterfaceInstanceName();
            netRecv = Track(new PerformanceCounter("Network Interface", "Bytes Received/sec", networkInterface));
            netSend = Track(new PerformanceCounter("Network Interface", "Bytes Sent/sec", networkInterface));

            ramTotalMB = GetTotalPhysicalMemoryMB();

            var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (_, _) => UpdateMetrics();
            timer.Start();

            cpuTotal.NextValue();
            ramAvailable.NextValue();
            diskRead.NextValue();
            diskWrite.NextValue();
            netRecv.NextValue();
            netSend.NextValue();
        }

        private T Track<T>(T counter) where T : IDisposable
        {
            disposableCounters.Add(counter);
            return counter;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var counter in disposableCounters)
                {
                    counter.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private static float GetTotalPhysicalMemoryMB()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                var result = searcher.Get().Cast<ManagementObject>().FirstOrDefault();

                if (result != null && result["TotalVisibleMemorySize"] is ulong sizeKB)
                {
                    return sizeKB / 1024f;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error retrieving RAM via WMI: {ex.Message}");
            }
            return 0;
        }

        private static string GetNetworkInterfaceInstanceName()
        {
            try
            {
                return new PerformanceCounterCategory("Network Interface")
                    .GetInstanceNames()
                    .Where(n => !n.Contains("Loopback", StringComparison.OrdinalIgnoreCase) &&
                                !n.Contains("Teredo", StringComparison.OrdinalIgnoreCase) &&
                                !n.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault() ?? "_Total";
            }
            catch (InvalidOperationException)
            {
                return "_Total";
            }
        }

        private void UpdateMetrics()
        {
            cpuUsage = cpuTotal.NextValue();
            ramUsedMB = ramTotalMB - ramAvailable.NextValue();
            diskMBs = (diskRead.NextValue() + diskWrite.NextValue()) / 1024f / 1024f;
            netKBs = (netRecv.NextValue() + netSend.NextValue()) / 1024f;

            Push(cpuHistory, cpuUsage);
            Push(ramHistory, ramUsedMB / ramTotalMB * 100f);
            Push(diskHistory, diskMBs);
            Push(netHistory, netKBs);

            tickCount++;
            if (tickCount % ProcessCheckInterval == 0 && !isDetectingSpikes)
            {
                isDetectingSpikes = true;

                Task.Run(() => DetectProcessSpikes())
                    .ContinueWith(t =>
                    {
                        isDetectingSpikes = false;
                    }, TaskScheduler.FromCurrentSynchronizationContext());
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int dynamicWidth = this.ClientSize.Width - 70;

            int currentY = 10;

            DrawText(g, $"CPU: {cpuUsage:F1}%", 10, currentY, Color.White);
            currentY += 20;
            DrawText(g, $"RAM: {ramUsedMB:F0}/{ramTotalMB:F0} MB ({ramUsedMB / ramTotalMB * 100f:F1}%)", 10, currentY, Color.White);
            currentY += 20;
            DrawText(g, $"Disk I/O: {diskMBs:F2} MB/s", 10, currentY, Color.White);
            currentY += 20;
            DrawText(g, $"Network: {netKBs:F1} KB/s", 10, currentY, Color.White);
            currentY += 20;

            currentY += 5;
            using (var pen = new Pen(Color.FromArgb(50, 50, 50), 1))
            {
                g.DrawLine(pen, 10, currentY, this.ClientSize.Width - 10, currentY);
            }
            currentY += 15;

            int top = currentY;
            DrawGraph(g, cpuHistory, top, 100f, Color.DodgerBlue, "CPU %", cpuUsage, dynamicWidth, this.ClientSize.Width);
            DrawGraph(g, ramHistory, top + 90, 100f, Color.LimeGreen, "RAM %", ramUsedMB / ramTotalMB * 100f, dynamicWidth, this.ClientSize.Width);
            DrawGraph(g, diskHistory, top + 180, 10f, Color.Orange, "Disk MB/s", diskMBs, dynamicWidth, this.ClientSize.Width);
            DrawGraph(g, netHistory, top + 270, 500f, Color.DeepSkyBlue, "Net KB/s", netKBs, dynamicWidth, this.ClientSize.Width);

            currentY = top + 360;

            DrawText(g, "Process CPU Spikes:", 10, currentY, Color.White);
            currentY += 20;

            using var boldFont = new Font("Segoe UI", 10, FontStyle.Bold);

            lock (spikeLock)
            {
                foreach (var s in spikes.OrderByDescending(s => s.Multiplier).Take(5))
                {
                    Color multiplierColor = s.Multiplier > 3.0f ? Color.Red : s.Multiplier > 2.5f ? Color.Yellow : Color.White;

                    string baseText = $"{s.Name} (PID {s.Pid}) {s.Current:F1}% baseline {s.Baseline:F1}%";
                    string multiplierText = $" ×{s.Multiplier:F1}";

                    DrawText(g, baseText, 10, currentY, Color.White);

                    float baseWidth = g.MeasureString(baseText, boldFont).Width;

                    DrawText(g, multiplierText, 10 + (int)baseWidth, currentY, multiplierColor);

                    currentY += 18;
                }
            }
        }

        private void DetectProcessSpikes()
        {
            var newSpikes = new List<ProcessSpike>();
            var now = DateTime.UtcNow;

            Process[] allProcesses;
            try
            {
                allProcesses = Process.GetProcesses();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is System.PlatformNotSupportedException)
            {
                return;
            }

            foreach (var p in allProcesses)
            {
                try
                {
                    if (p.HasExited || p.TotalProcessorTime.TotalSeconds <= 1) continue;

                    float currentCpuUsage = 0f;

                    using (var pc = new PerformanceCounter("Process", "% Processor Time", p.ProcessName, true))
                    {
                        currentCpuUsage = pc.NextValue() / Environment.ProcessorCount;
                    }

                    if (!processBaselines.TryGetValue(p.Id, out var baselineData) ||
                        (now - baselineData.LastChecked).TotalSeconds > ProcessHistorySize)
                    {
                        processBaselines[p.Id] = (currentCpuUsage, now);
                        continue;
                    }

                    const float alpha = 0.1f;
                    float oldBaseline = baselineData.AvgCpu;
                    float newBaseline = (alpha * currentCpuUsage) + ((1 - alpha) * oldBaseline);

                    processBaselines[p.Id] = (newBaseline, now);

                    float baseline = newBaseline;

                    if (baseline > 1f && currentCpuUsage > baseline * 2.5f && currentCpuUsage > 5f)
                    {
                        newSpikes.Add(new ProcessSpike
                        {
                            Name = p.ProcessName,
                            Pid = p.Id,
                            Current = currentCpuUsage,
                            Baseline = baseline,
                            Multiplier = currentCpuUsage / baseline
                        });
                    }
                }
                catch
                {
                }
            }

            var currentPids = allProcesses.Select(p => p.Id).ToHashSet();
            var pidsToRemove = processBaselines.Keys.Except(currentPids).ToList();
            foreach (var pid in pidsToRemove)
            {
                processBaselines.Remove(pid);
            }

            lock (spikeLock)
            {
                spikes.Clear();
                spikes.AddRange(newSpikes);
            }
        }

        private static void Push(Queue<float> q, float v)
        {
            q.Enqueue(v);
            if (q.Count > HistorySize) q.Dequeue();
        }

        private static void DrawText(Graphics g, string t, int x, int y, Color c)
        {
            using var f = new Font("Segoe UI", 10, FontStyle.Bold);
            using var b = new SolidBrush(c);
            g.DrawString(t, f, b, x, y);
        }

        private static void DrawGraph(Graphics g, Queue<float> data, int y, float max, Color c, string label, float currentValue, int w, int formWidth)
        {
            int h = 60;
            int x = 10;

            g.DrawString(label, SystemFonts.DefaultFont, Brushes.Gray, x, y - 12);

            if (data.Count < 2) return;

            float[] arr = data.ToArray();

            using var path = new GraphicsPath();
            using var pen = new Pen(c, 2);
            using var gridPen = new Pen(Color.FromArgb(50, 50, 50), 1);

            for (int i = 1; i < 4; i++)
            {
                float gridY = y + h - (h * i / 4.0f);
                g.DrawLine(gridPen, x, gridY, x + w, gridY);
            }

            g.DrawLine(gridPen, x, y + h, x + w, y + h);

            PointF lastPoint = PointF.Empty;
            for (int i = 0; i < arr.Length; i++)
            {
                float x_coord = x + i * (w / (float)HistorySize);
                float value_clamped = Math.Min(arr[i], max);
                float y_coord = y + h - (value_clamped / max * h);

                if (i > 0)
                {
                    path.AddLine(lastPoint.X, lastPoint.Y, x_coord, y_coord);
                }
                lastPoint = new PointF(x_coord, y_coord);
            }
            g.DrawPath(pen, path);

            using var brush = new SolidBrush(c);
            float currentX = x + w;
            float currentY = y + h - (Math.Min(currentValue, max) / max * h);

            g.FillEllipse(brush, currentX - 4, currentY - 4, 8, 8);

            int textMarginRight = formWidth - 5;

            DrawSmallText(g, $"Max: {max:F0}{(max == 100f ? "%" : "")}", textMarginRight - 40, y + 5, c);
        }

        private static void DrawSmallText(Graphics g, string t, int x, int y, Color c)
        {
            using var f = new Font("Segoe UI", 8);
            using var b = new SolidBrush(c);
            g.DrawString(t, f, b, x, y);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
                ExportSnapshot();
            base.OnKeyDown(e);
        }

        private void ExportSnapshot()
        {
            var snapshot = new
            {
                Timestamp = DateTime.UtcNow,
                CPU = cpuUsage,
                RAM_MB = ramUsedMB,
                RAM_Total_MB = ramTotalMB,
                Disk_MBps = diskMBs,
                Network_KBps = netKBs,
                Spikes = spikes.OrderByDescending(s => s.Multiplier).Take(5)
            };

            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            File.WriteAllText($"hostpulse_snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.json", json);
        }
    }

    public class ProcessSpike
    {
        public required string Name { get; set; }
        public required int Pid { get; set; }
        public required float Current { get; set; }
        public required float Baseline { get; set; }
        public required float Multiplier { get; set; }
    }
}