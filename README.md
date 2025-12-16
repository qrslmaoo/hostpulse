#  HostPulse: Real-Time Host Telemetry and Spike Detection

**HostPulse** is a lightweight, cross-platform utility built with C# and Windows Forms (System.Drawing/System.Windows.Forms) that provides a real-time, low-overhead graphical monitor for system resource utilization and detects abnormal CPU spikes in running processes.

It is designed to give developers and power users a quick, uncluttered view of system performance without the resource demands of larger monitoring tools.

##  Features

* **Real-Time Metrics:** Continuously monitor CPU usage, RAM usage, Disk I/O (MB/s), and Network Traffic (KB/s).
* **Performance History Graphs:** Visualize the last 120 seconds of performance data with auto-scaling graphs.
* **Asynchronous Spike Detection:** Uses a background thread to calculate Exponential Moving Averages (EMA) for process CPU usage, efficiently detecting processes consuming significantly more CPU than their typical baseline.
* **Low Overhead:** Built on native Windows Performance Counters for high-efficiency data collection.
* **Dynamic UI:** The telemetry graphs resize automatically with the window for optimal viewing.

##  Getting Started

### Prerequisites

* .NET SDK (6.0 or higher)
* Windows operating system (Requires access to native Windows Performance Counters).

### Installation (Building from Source)

1.  **Clone the repository:**
    ```bash
    git clone [https://github.com/qrslmaoo/hostpulse.git]
    cd hostpulse/src/HostPulse.UI
    ```

2.  **Build and Run:**
    ```bash
    dotnet run
    ```

The HostPulse GUI window should launch, immediately beginning to collect and display telemetry data.

##  Usage

| Area | Description |
| :--- | :--- |
| **Top Section** | Shows immediate, current values for CPU, RAM, Disk I/O, and Network activity. |
| **History Graphs** | Four self-scaling graphs showing the performance over the last 120 seconds. The Max value is dynamically displayed on the right edge. |
| **Process CPU Spikes** | Lists the top 5 processes that are currently exhibiting a high CPU usage **relative to their own historical average (baseline)**. |
| **Color Coding** | Spikes are highlighted: **Yellow** (moderate spike) or **Red** (severe spike, multiplier > 3.0x). |

### Exporting Data Snapshot

Press `Ctrl + S` at any time while the HostPulse window is focused.

A JSON file named `hostpulse_snapshot_YYYYMMDD_HHMMSS.json` will be saved to the application directory, containing a detailed record of the current resource usage and all detected process spikes.

##  Technical Details

HostPulse utilizes the following core technologies for its operation:

* **System.Diagnostics.PerformanceCounter:** Used to collect high-frequency data for CPU, RAM, Disk, and Network without polling external APIs.
* **System.Management (WMI):** Used once at startup to determine the total physical RAM size.
* **Asynchronous Processing:** The performance-intensive task of iterating through all running processes and updating baselines is handled off the main thread using `Task.Run()` to maintain a smooth, responsive UI.
* **Spike Detection Logic:** Baselines are established using an Exponential Moving Average (EMA) with an alpha of **0.1f**. A spike is detected if a process is using $>5\%$ CPU and its current usage is $>2.5$ times its baseline.

##  License

Distributed under the MIT License. See `LICENSE` for more information.

##  Contact

Your Name/Email - qrs | whenitsquirts@gmail.com