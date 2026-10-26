using System.Net.NetworkInformation;
using System.Windows.Threading;
using Microsoft.Win32;
using Whitebox.Core;

namespace Whitebox.Services;

public static class SystemInfo
{
    /// <summary>Shows placeholder names instead of the real user and machine (for screenshots).</summary>
    public static bool DemoMode { get; set; }

    public static string MachineName => DemoMode ? "DESKTOP-WHITEBOX" : Environment.MachineName;
    public static string UserName => DemoMode ? "Alex" : Environment.UserName;
    public static int Cores => Environment.ProcessorCount;

    public static string CpuName { get; } = ReadCpuName();
    public static string OsName { get; } = ReadOsName();

    private static string ReadCpuName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var name = (k?.GetValue("ProcessorNameString") as string)?.Trim();
            if (!string.IsNullOrEmpty(name))
                return System.Text.RegularExpressions.Regex.Replace(name.Replace("(R)", "").Replace("(TM)", "").Replace(" CPU", ""), @"\s+", " ").Trim();
        }
        catch { }
        return "Processor";
    }

    private static string ReadOsName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = k?.GetValue("ProductName") as string ?? "Windows";
            var display = k?.GetValue("DisplayVersion") as string;
            if (Environment.OSVersion.Version.Build >= 22000) product = product.Replace("Windows 10", "Windows 11");
            return display != null ? $"{product} {display}" : product;
        }
        catch
        {
            return "Windows";
        }
    }

    public static DriveInfo SystemDrive => new(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");

    public static IEnumerable<DriveInfo> FixedDrives() =>
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady);
}

/// <summary>Samples CPU, memory and network once per second for the whole app.</summary>
public sealed class SystemMonitor : ObservableObject
{
    public static SystemMonitor Instance { get; } = new();

    private const int HistoryLength = 60;
    private readonly Queue<double> _cpu = new(), _ram = new(), _down = new(), _up = new();
    private long _prevIdle, _prevKernel, _prevUser;
    private long _prevRx, _prevTx;
    private DateTime _prevNetTime;
    private readonly DispatcherTimer _timer;
    private int _samples;

    private SystemMonitor()
    {
        for (int i = 0; i < HistoryLength; i++)
        {
            _cpu.Enqueue(0); _ram.Enqueue(0); _down.Enqueue(0); _up.Enqueue(0);
        }
        Sample();
        Thread.Sleep(150);
        Sample();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
    }

    public double Cpu { get; private set; }
    public double RamPercent { get; private set; }
    public long RamTotal { get; private set; }
    public long RamUsed { get; private set; }
    public long RamAvailable { get; private set; }
    public long CommitUsed { get; private set; }
    public long CommitLimit { get; private set; }
    public double NetDown { get; private set; }
    public double NetUp { get; private set; }
    public string Uptime { get; private set; } = "";

    public double[] CpuHistory { get; private set; } = [];
    public double[] RamHistory { get; private set; } = [];
    public double[] DownHistory { get; private set; } = [];
    public double[] UpHistory { get; private set; } = [];

    public string CpuText => $"{Cpu:0}%";
    public string RamText => $"{Fmt.Bytes(RamUsed)} / {Fmt.Bytes(RamTotal)}";
    public string DownText => Fmt.Speed(NetDown);
    public string UpText => Fmt.Speed(NetUp);

    private static void Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > HistoryLength) q.Dequeue();
    }

    private void Sample()
    {
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            long di = idle - _prevIdle, dk = kernel - _prevKernel, du = user - _prevUser;
            long total = dk + du;
            if (_prevKernel != 0 && total > 0) Cpu = Math.Clamp((total - di) * 100.0 / total, 0, 100);
            _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
        }

        var m = Native.Memory();
        RamTotal = (long)m.ullTotalPhys;
        RamAvailable = (long)m.ullAvailPhys;
        RamUsed = RamTotal - RamAvailable;
        RamPercent = RamTotal > 0 ? RamUsed * 100.0 / RamTotal : 0;
        CommitLimit = (long)m.ullTotalPageFile;
        CommitUsed = (long)(m.ullTotalPageFile - m.ullAvailPageFile);

        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = ni.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
        }
        catch { }

        var now = DateTime.UtcNow;
        if (_prevNetTime != default)
        {
            double secs = Math.Max(0.1, (now - _prevNetTime).TotalSeconds);
            NetDown = Math.Max(0, (rx - _prevRx) / secs);
            NetUp = Math.Max(0, (tx - _prevTx) / secs);
        }
        _prevRx = rx; _prevTx = tx; _prevNetTime = now;

        Uptime = Fmt.Duration(TimeSpan.FromMilliseconds(Environment.TickCount64));

        // The first real CPU/network delta is only available on the second sample; seed the history
        // with it so charts start flat instead of ramping up from zero.
        if (++_samples == 2)
            for (int i = 0; i < HistoryLength; i++)
            {
                Push(_cpu, Cpu); Push(_ram, RamPercent); Push(_down, NetDown); Push(_up, NetUp);
            }
        Push(_cpu, Cpu); Push(_ram, RamPercent); Push(_down, NetDown); Push(_up, NetUp);
        CpuHistory = _cpu.ToArray();
        RamHistory = _ram.ToArray();
        DownHistory = _down.ToArray();
        UpHistory = _up.ToArray();

        OnPropertyChanged(string.Empty);
    }
}
