using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace GpuSentinel
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] commandLine)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs args)
            {
                TryWriteCrashLog(args.Exception);
                MessageBox.Show(
                    "监控器发生错误：\r\n\r\n" + args.Exception.Message,
                    "GPU Sentinel",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args)
            {
                TryWriteCrashLog(args.ExceptionObject as Exception);
            };
            bool showDeviceSelector = commandLine != null && commandLine.Any(item =>
                string.Equals(item, "--select-devices", StringComparison.OrdinalIgnoreCase));
            Application.Run(new MainForm(showDeviceSelector));
        }

        private static void TryWriteCrashLog(Exception exception)
        {
            if (exception == null)
            {
                return;
            }
            try
            {
                string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                    " " + exception + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    internal static class Palette
    {
        public static readonly Color Background = Color.FromArgb(18, 20, 24);
        public static readonly Color Surface = Color.FromArgb(27, 30, 35);
        public static readonly Color SurfaceRaised = Color.FromArgb(33, 37, 43);
        public static readonly Color Border = Color.FromArgb(53, 58, 66);
        public static readonly Color Text = Color.FromArgb(236, 239, 242);
        public static readonly Color Muted = Color.FromArgb(151, 160, 171);
        public static readonly Color Dim = Color.FromArgb(101, 109, 119);
        public static readonly Color Green = Color.FromArgb(118, 185, 0);
        public static readonly Color Cyan = Color.FromArgb(70, 197, 230);
        public static readonly Color Amber = Color.FromArgb(230, 180, 80);
        public static readonly Color Red = Color.FromArgb(239, 106, 106);
        public static readonly Color Blue = Color.FromArgb(103, 160, 255);
        public static readonly Color Grid = Color.FromArgb(45, 49, 57);
    }

    internal enum HealthLevel
    {
        Healthy,
        Warning,
        Error,
        Offline
    }

    internal sealed class AppConfig
    {
        public int RefreshSeconds;
        public int HistoryPoints;
        public int AlertTemperature;
        public int AlertPower;
        public bool StartMinimized;
        public string LlamaUrl;
        public string LlamaPortable;
        public List<string> SelectedDevices;
        public string ConfigPath;

        public AppConfig()
        {
            RefreshSeconds = 2;
            HistoryPoints = 120;
            AlertTemperature = 82;
            AlertPower = 250;
            StartMinimized = false;
            LlamaUrl = "http://127.0.0.1:8080";
            LlamaPortable = string.Empty;
            SelectedDevices = new List<string>();
            ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "V100Monitor.ini");
        }

        public static AppConfig Load()
        {
            AppConfig result = new AppConfig();
            try
            {
                if (!File.Exists(result.ConfigPath))
                {
                    return result;
                }
                string section = string.Empty;
                foreach (string rawLine in File.ReadAllLines(result.ConfigPath, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    {
                        continue;
                    }
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                        continue;
                    }
                    int split = line.IndexOf('=');
                    if (split <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, split).Trim().ToLowerInvariant();
                    string value = line.Substring(split + 1).Trim();
                    if (section == "monitor")
                    {
                        if (key == "refresh_seconds")
                        {
                            int parsed;
                            if (int.TryParse(value, out parsed))
                            {
                                result.RefreshSeconds = Math.Max(1, Math.Min(30, parsed));
                            }
                        }
                        else if (key == "history_points")
                        {
                            int parsed;
                            if (int.TryParse(value, out parsed))
                            {
                                result.HistoryPoints = Math.Max(30, Math.Min(600, parsed));
                            }
                        }
                        else if (key == "alert_temperature")
                        {
                            int.TryParse(value, out result.AlertTemperature);
                        }
                        else if (key == "alert_power")
                        {
                            int.TryParse(value, out result.AlertPower);
                        }
                        else if (key == "selected_devices")
                        {
                            result.SelectedDevices.Clear();
                            foreach (string item in value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                string trimmed = item.Trim();
                                if (trimmed.Length > 0)
                                {
                                    result.SelectedDevices.Add(trimmed);
                                }
                            }
                        }
                    }
                    else if (section == "ui")
                    {
                        if (key == "start_minimized")
                        {
                            result.StartMinimized = value == "1" ||
                                value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (key == "llama_url")
                        {
                            result.LlamaUrl = value;
                        }
                    }
                    else if (section == "paths" && key == "llama_portable")
                    {
                        result.LlamaPortable = value;
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        public void SaveSelectedDevices(IEnumerable<string> deviceIds)
        {
            SelectedDevices = (deviceIds ?? Enumerable.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<string> lines = new List<string>();
            lines.Add("[monitor]");
            lines.Add("refresh_seconds=" + RefreshSeconds.ToString(CultureInfo.InvariantCulture));
            lines.Add("history_points=" + HistoryPoints.ToString(CultureInfo.InvariantCulture));
            lines.Add("alert_temperature=" + AlertTemperature.ToString(CultureInfo.InvariantCulture));
            lines.Add("alert_power=" + AlertPower.ToString(CultureInfo.InvariantCulture));
            lines.Add("selected_devices=" + string.Join(";", SelectedDevices));
            lines.Add(string.Empty);
            lines.Add("[ui]");
            lines.Add("start_minimized=" + (StartMinimized ? "true" : "false"));
            lines.Add("llama_url=" + LlamaUrl);
            lines.Add(string.Empty);
            lines.Add("[paths]");
            lines.Add("llama_portable=" + LlamaPortable);
            File.WriteAllLines(ConfigPath, lines, new UTF8Encoding(false));
        }
    }

    internal sealed class GpuReading
    {
        public string DeviceKey;
        public string Vendor;
        public bool TelemetryAvailable;
        public string Role;
        public string Name;
        public string StatusText;
        public string StatusDetail;
        public HealthLevel Health;
        public bool Present;
        public bool DriverStarted;
        public int ProblemCode;
        public string DriverVersion;
        public string InfName;
        public string Mode;
        public string PendingMode;
        public string PState;
        public string PciBus;
        public string Pcie;
        public string Uuid;
        public string Ecc;
        public double UsagePercent;
        public double MemoryUsedMiB;
        public double MemoryTotalMiB;
        public double TemperatureC;
        public double PowerDrawW;
        public double PowerLimitW;
        public double MemoryUtilizationPercent;
        public double GraphicsClockMHz;
        public double MemoryClockMHz;
        public string EventMessage;

        public GpuReading(string role)
        {
            DeviceKey = string.Empty;
            Vendor = "Other";
            TelemetryAvailable = false;
            Role = role;
            Name = role;
            StatusText = "未检测";
            StatusDetail = string.Empty;
            Health = HealthLevel.Offline;
            DriverVersion = "--";
            InfName = "--";
            Mode = "--";
            PendingMode = "--";
            PState = "--";
            PciBus = "--";
            Pcie = "--";
            Uuid = "--";
            Ecc = "--";
            EventMessage = string.Empty;
        }

        public double MemoryUsagePercent
        {
            get
            {
                if (MemoryTotalMiB <= 0)
                {
                    return 0;
                }
                return Math.Max(0, Math.Min(100, MemoryUsedMiB / MemoryTotalMiB * 100));
            }
        }
    }

    internal sealed class RawTelemetry
    {
        public string Name;
        public string Uuid;
        public string PciBus;
        public string Pcie;
        public string DriverVersion;
        public string Mode;
        public string PendingMode;
        public string PState;
        public string Ecc;
        public double UsagePercent;
        public double MemoryTotalMiB;
        public double MemoryUsedMiB;
        public double MemoryFreeMiB;
        public double TemperatureC;
        public double PowerDrawW;
        public double PowerLimitW;
        public double MemoryUtilizationPercent;
        public double GraphicsClockMHz;
        public double MemoryClockMHz;
    }

    internal sealed class PnpReading
    {
        public bool Present;
        public string Name;
        public string InstanceId;
        public string Vendor;
        public string Status;
        public int ProblemCode;
        public string DriverVersion;
        public string InfName;
        public string ProblemText;

        public PnpReading()
        {
            Name = string.Empty;
            InstanceId = string.Empty;
            Vendor = "Other";
            Status = "Unknown";
            DriverVersion = string.Empty;
            InfName = string.Empty;
            ProblemText = string.Empty;
        }
    }

    internal sealed class SmiResult
    {
        public readonly List<RawTelemetry> Devices = new List<RawTelemetry>();
        public string Error;
    }

    internal sealed class MonitorSnapshot
    {
        public DateTime Timestamp;
        public readonly List<GpuReading> Devices = new List<GpuReading>();
        public string OverallText;
        public HealthLevel OverallHealth;
        public string RawError;
        public string DriverEvent;
    }

    internal sealed class HistorySample
    {
        public DateTime Timestamp;
        public readonly Dictionary<string, double> Usage = new Dictionary<string, double>();
        public readonly Dictionary<string, double> Temperature = new Dictionary<string, double>();
    }

    internal sealed class LogItem
    {
        public DateTime Timestamp;
        public HealthLevel Level;
        public string Message;
    }

    internal static class Formatters
    {
        public static string Number(double value, string suffix, int decimals)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                return "--";
            }
            return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) +
                suffix;
        }

        public static string Percent(double value)
        {
            return Number(value, "%", 0);
        }

        public static string Memory(double used, double total)
        {
            if (total <= 0)
            {
                return "--";
            }
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:F1} / {1:F0} GB",
                used / 1024.0,
                total / 1024.0);
        }

        public static string Time(DateTime time)
        {
            return time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }

    internal static class NvidiaSmiReader
    {
        private const string Query =
            "index,name,uuid,pci.bus_id,driver_version,driver_model.current,driver_model.pending," +
            "pstate,temperature.gpu,power.draw,power.limit,memory.total,memory.used,memory.free," +
            "utilization.gpu,utilization.memory,pcie.link.gen.current,pcie.link.width.current," +
            "clocks.current.graphics,clocks.current.memory,ecc.mode.current";

        public static SmiResult Read()
        {
            SmiResult result = new SmiResult();
            string smi = FindSmi();
            if (smi == null)
            {
                result.Error = "未找到 nvidia-smi.exe";
                return result;
            }

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = smi;
            info.Arguments = "--query-gpu=" + Query + " --format=csv,noheader,nounits";
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = info;
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(7000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }
                        result.Error = "nvidia-smi 超时";
                        return result;
                    }

                    foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string line = rawLine.Trim();
                        int first = line.Length > 0 ? line[0] : 'x';
                        if (first < '0' || first > '9')
                        {
                            continue;
                        }
                        string[] columns = line.Split(',');
                        if (columns.Length < 21)
                        {
                            continue;
                        }
                        RawTelemetry telemetry = new RawTelemetry();
                        telemetry.Name = Clean(columns[1]);
                        telemetry.Uuid = Clean(columns[2]);
                        telemetry.PciBus = Clean(columns[3]);
                        telemetry.DriverVersion = Clean(columns[4]);
                        telemetry.Mode = Clean(columns[5]);
                        telemetry.PendingMode = Clean(columns[6]);
                        telemetry.PState = Clean(columns[7]);
                        telemetry.TemperatureC = ParseDouble(columns[8]);
                        telemetry.PowerDrawW = ParseDouble(columns[9]);
                        telemetry.PowerLimitW = ParseDouble(columns[10]);
                        telemetry.MemoryTotalMiB = ParseDouble(columns[11]);
                        telemetry.MemoryUsedMiB = ParseDouble(columns[12]);
                        telemetry.MemoryFreeMiB = ParseDouble(columns[13]);
                        telemetry.UsagePercent = ParseDouble(columns[14]);
                        telemetry.MemoryUtilizationPercent = ParseDouble(columns[15]);
                        string pcieGeneration = Clean(columns[16]);
                        string pcieWidth = Clean(columns[17]);
                        telemetry.Pcie = IsUnavailable(pcieGeneration) || IsUnavailable(pcieWidth)
                            ? "--"
                            : "Gen" + pcieGeneration + " x" + pcieWidth;
                        telemetry.GraphicsClockMHz = ParseDouble(columns[18]);
                        telemetry.MemoryClockMHz = ParseDouble(columns[19]);
                        telemetry.Ecc = Clean(columns[20]);
                        result.Devices.Add(telemetry);
                    }
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        result.Error = error.Trim();
                    }
                }
            }
            catch (Exception exception)
            {
                result.Error = exception.Message;
            }
            return result;
        }

        private static string FindSmi()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string systemSmi = Path.Combine(windows, "System32", "nvidia-smi.exe");
            if (File.Exists(systemSmi))
            {
                return systemSmi;
            }
            string legacySmi = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation",
                "NVSMI",
                "nvidia-smi.exe");
            return File.Exists(legacySmi) ? legacySmi : null;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static double ParseDouble(string value)
        {
            string text = Clean(value);
            if (IsUnavailable(text))
            {
                return -1;
            }
            double parsed;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : -1;
        }

        private static bool IsUnavailable(string value)
        {
            string text = Clean(value);
            return text.Length == 0 ||
                text.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("[N/A]", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Not Supported", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class PnpReader
    {
        public static List<PnpReading> DiscoverDisplays()
        {
            List<PnpReading> devices = new List<PnpReading>();
            List<DriverInfo> drivers = new List<DriverInfo>();

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT DeviceName,DriverVersion,InfName,DeviceID FROM Win32_PnPSignedDriver WHERE DeviceClass = 'DISPLAY'"))
                using (ManagementObjectCollection collection = searcher.Get())
                {
                    foreach (ManagementObject item in collection)
                    {
                        using (item)
                        {
                            drivers.Add(new DriverInfo
                            {
                                DeviceId = Convert.ToString(item["DeviceID"], CultureInfo.InvariantCulture),
                                Name = Convert.ToString(item["DeviceName"], CultureInfo.InvariantCulture),
                                Version = Convert.ToString(item["DriverVersion"], CultureInfo.InvariantCulture),
                                Inf = Convert.ToString(item["InfName"], CultureInfo.InvariantCulture)
                            });
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Name,PNPDeviceID,Status,ConfigManagerErrorCode,PNPClass FROM Win32_PnPEntity WHERE PNPClass = 'Display'"))
                using (ManagementObjectCollection collection = searcher.Get())
                {
                    foreach (ManagementObject item in collection)
                    {
                        using (item)
                        {
                            string instanceId = Convert.ToString(item["PNPDeviceID"], CultureInfo.InvariantCulture);
                            if (string.IsNullOrWhiteSpace(instanceId) ||
                                !instanceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            PnpReading reading = new PnpReading();
                            reading.Present = true;
                            reading.Name = Convert.ToString(item["Name"], CultureInfo.InvariantCulture);
                            reading.InstanceId = instanceId;
                            reading.Status = Convert.ToString(item["Status"], CultureInfo.InvariantCulture);
                            object code = item["ConfigManagerErrorCode"];
                            reading.ProblemCode = code == null
                                ? 0
                                : Convert.ToInt32(code, CultureInfo.InvariantCulture);
                            reading.ProblemText = DescribeProblem(reading.ProblemCode);
                            reading.Vendor = DetectVendor(reading.Name, instanceId);

                            DriverInfo driver = drivers.FirstOrDefault(candidate =>
                                string.Equals(candidate.DeviceId, instanceId, StringComparison.OrdinalIgnoreCase));
                            if (driver == null)
                            {
                                driver = drivers.FirstOrDefault(candidate =>
                                    !string.IsNullOrWhiteSpace(candidate.Name) &&
                                    string.Equals(candidate.Name, reading.Name, StringComparison.OrdinalIgnoreCase));
                            }
                            if (driver != null)
                            {
                                reading.DriverVersion = driver.Version;
                                reading.InfName = driver.Inf;
                            }
                            devices.Add(reading);
                        }
                    }
                }
            }
            catch
            {
            }

            return devices
                .OrderBy(item => VendorRank(item.Vendor))
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static string DetectVendor(string name, string instanceId)
        {
            string combined = (name ?? string.Empty) + " " + (instanceId ?? string.Empty);
            if (combined.IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("Tesla", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("Quadro", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "NVIDIA";
            }
            if (combined.IndexOf("VEN_8086", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Intel";
            }
            if (combined.IndexOf("VEN_1002", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("VEN_1022", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "AMD";
            }
            return "Other";
        }

        public static int VendorRank(string vendor)
        {
            if (string.Equals(vendor, "NVIDIA", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(vendor, "Intel", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(vendor, "AMD", StringComparison.OrdinalIgnoreCase)) return 2;
            return 3;
        }

        private sealed class DriverInfo
        {
            public string DeviceId;
            public string Name;
            public string Version;
            public string Inf;
        }

        public static PnpReading Read(string deviceToken, string friendlyToken)
        {
            PnpReading reading = new PnpReading();
            try
            {
                string query = "SELECT * FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%" + deviceToken + "%'";
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                using (ManagementObjectCollection devices = searcher.Get())
                {
                    foreach (ManagementObject device in devices)
                    {
                        using (device)
                        {
                            string name = Convert.ToString(device["Name"], CultureInfo.InvariantCulture);
                            if (friendlyToken.Length > 0 && name.IndexOf(friendlyToken, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }
                            reading.Present = true;
                            reading.Name = name;
                            reading.InstanceId = Convert.ToString(device["PNPDeviceID"], CultureInfo.InvariantCulture);
                            reading.Status = Convert.ToString(device["Status"], CultureInfo.InvariantCulture);
                            object code = device["ConfigManagerErrorCode"];
                            reading.ProblemCode = code == null ? 0 : Convert.ToInt32(code, CultureInfo.InvariantCulture);
                            reading.ProblemText = DescribeProblem(reading.ProblemCode);
                            break;
                        }
                    }
                }

                if (reading.Present)
                {
                    string driverQuery =
                        "SELECT DeviceName,DriverVersion,InfName,DeviceID FROM Win32_PnPSignedDriver " +
                        "WHERE DeviceID LIKE '%" + deviceToken + "%'";
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(driverQuery))
                    using (ManagementObjectCollection drivers = searcher.Get())
                    {
                        foreach (ManagementObject driver in drivers)
                        {
                            using (driver)
                            {
                                string name = Convert.ToString(driver["DeviceName"], CultureInfo.InvariantCulture);
                                if (friendlyToken.Length > 0 &&
                                    name.IndexOf(friendlyToken, StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    continue;
                                }
                                reading.DriverVersion = Convert.ToString(driver["DriverVersion"], CultureInfo.InvariantCulture);
                                reading.InfName = Convert.ToString(driver["InfName"], CultureInfo.InvariantCulture);
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return reading;
        }

        public static string DescribeProblem(int code)
        {
            switch (code)
            {
                case 0:
                    return "正常";
                case 10:
                    return "代码 10：设备无法启动";
                case 12:
                    return "代码 12：系统资源不足";
                case 14:
                    return "代码 14：需要重启";
                case 18:
                    return "代码 18：需要重新安装驱动";
                case 22:
                    return "设备已禁用";
                case 28:
                    return "未安装驱动程序";
                case 31:
                    return "代码 31：驱动无法加载";
                case 43:
                    return "代码 43：驱动已停止";
                default:
                    return "设备代码 " + code.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    internal sealed class MonitorService
    {
        private DateTime lastPnpRefresh = DateTime.MinValue;
        private List<PnpReading> cachedPnp = new List<PnpReading>();
        private string cachedEvent;
        private DateTime lastEventRefresh = DateTime.MinValue;
        private readonly object syncRoot = new object();

        public List<PnpReading> GetAvailableDevices()
        {
            lock (syncRoot)
            {
                cachedPnp = PnpReader.DiscoverDisplays();
                lastPnpRefresh = DateTime.Now;
                return cachedPnp.Select(item => item).ToList();
            }
        }

        public void InvalidatePnpCache()
        {
            lock (syncRoot)
            {
                lastPnpRefresh = DateTime.MinValue;
            }
        }

        public MonitorSnapshot Capture(AppConfig config)
        {
            lock (syncRoot)
            {
                DateTime now = DateTime.Now;
                bool refreshPnp = cachedPnp.Count == 0 ||
                    (now - lastPnpRefresh).TotalSeconds >= 5;
                if (refreshPnp)
                {
                    cachedPnp = PnpReader.DiscoverDisplays();
                    lastPnpRefresh = now;
                }

                SmiResult smi = NvidiaSmiReader.Read();
                HashSet<string> selected = new HashSet<string>(
                    config.SelectedDevices ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                HashSet<RawTelemetry> usedRaw = new HashSet<RawTelemetry>();

                MonitorSnapshot snapshot = new MonitorSnapshot();
                snapshot.Timestamp = now;
                foreach (PnpReading pnp in cachedPnp)
                {
                    if (selected.Count > 0 && !selected.Contains(pnp.InstanceId))
                    {
                        continue;
                    }

                    RawTelemetry raw = null;
                    if (string.Equals(pnp.Vendor, "NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        raw = FindRawTelemetry(pnp, smi.Devices, usedRaw);
                    }
                    snapshot.Devices.Add(Merge(pnp, raw));
                }

                snapshot.RawError = smi.Error;
                bool nvidiaProblem = snapshot.Devices.Any(item =>
                    string.Equals(item.Vendor, "NVIDIA", StringComparison.OrdinalIgnoreCase) &&
                    item.Health != HealthLevel.Healthy);
                if ((now - lastEventRefresh).TotalSeconds >= 20 && nvidiaProblem)
                {
                    cachedEvent = ReadRecentDriverEvent();
                    lastEventRefresh = now;
                }
                foreach (GpuReading reading in snapshot.Devices)
                {
                    if (string.Equals(reading.Vendor, "NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        reading.EventMessage = cachedEvent;
                    }
                }
                snapshot.DriverEvent = cachedEvent;

                if (snapshot.Devices.Count == 0)
                {
                    snapshot.OverallText = "未发现可监视显卡";
                    snapshot.OverallHealth = HealthLevel.Error;
                }
                else if (snapshot.Devices.Any(item => item.Health == HealthLevel.Error))
                {
                    snapshot.OverallText = "存在显卡驱动异常";
                    snapshot.OverallHealth = HealthLevel.Error;
                }
                else if (snapshot.Devices.Any(item => item.Health == HealthLevel.Offline))
                {
                    snapshot.OverallText = "部分显卡未连接";
                    snapshot.OverallHealth = HealthLevel.Offline;
                }
                else if (snapshot.Devices.Any(item => item.Health == HealthLevel.Warning))
                {
                    snapshot.OverallText = "部分显卡仅基础监视";
                    snapshot.OverallHealth = HealthLevel.Warning;
                }
                else
                {
                    snapshot.OverallText = snapshot.Devices.Count.ToString(CultureInfo.InvariantCulture) + " 张显卡运行正常";
                    snapshot.OverallHealth = HealthLevel.Healthy;
                }
                return snapshot;
            }
        }

        private static RawTelemetry FindRawTelemetry(
            PnpReading pnp, IList<RawTelemetry> devices, ISet<RawTelemetry> used)
        {
            string target = NormalizeGpuName(pnp.Name);
            foreach (RawTelemetry device in devices)
            {
                if (used.Contains(device) || string.IsNullOrWhiteSpace(device.Name))
                {
                    continue;
                }
                string candidate = NormalizeGpuName(device.Name);
                if (target.Length > 0 && candidate.Length > 0 &&
                    (target.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     candidate.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    used.Add(device);
                    return device;
                }
            }
            return null;
        }

        private static string NormalizeGpuName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            string normalized = value.ToLowerInvariant();
            foreach (string token in new[]
            {
                "nvidia", "geforce", "tesla", "laptop", "gpu", "graphics",
                "corporation", "intel(r)", "amd"
            })
            {
                normalized = normalized.Replace(token, string.Empty);
            }
            return new string(normalized.Where(char.IsLetterOrDigit).ToArray());
        }

        private static GpuReading Merge(PnpReading pnp, RawTelemetry raw)
        {
            string vendor = pnp == null ? "Other" : pnp.Vendor;
            GpuReading result = new GpuReading(vendor.ToUpperInvariant());
            result.DeviceKey = pnp == null ? string.Empty : pnp.InstanceId;
            result.Vendor = vendor;
            result.TelemetryAvailable = raw != null;
            result.Present = pnp != null && pnp.Present;
            result.DriverStarted = result.Present && pnp.ProblemCode == 0;
            result.ProblemCode = pnp == null ? 0 : pnp.ProblemCode;
            result.StatusDetail = pnp == null ? string.Empty : pnp.ProblemText;
            result.DriverVersion = raw != null ? raw.DriverVersion : (pnp == null ? "--" : Fallback(pnp.DriverVersion));
            result.InfName = pnp == null ? "--" : Fallback(pnp.InfName);
            result.Name = raw != null && raw.Name.Length > 0
                ? raw.Name
                : (pnp != null && pnp.Name.Length > 0 ? pnp.Name : vendor + " GPU");
            result.Mode = raw != null ? Fallback(raw.Mode) : "--";
            result.PendingMode = raw != null ? Fallback(raw.PendingMode) : "--";
            result.PState = raw != null ? Fallback(raw.PState) : "--";
            result.PciBus = raw != null ? Fallback(raw.PciBus) : "--";
            result.Pcie = raw != null ? Fallback(raw.Pcie) : "--";
            result.Uuid = raw != null ? Fallback(raw.Uuid) : "--";
            result.Ecc = raw != null ? Fallback(raw.Ecc) : "--";

            if (raw != null)
            {
                result.UsagePercent = raw.UsagePercent;
                result.MemoryTotalMiB = raw.MemoryTotalMiB;
                result.MemoryUsedMiB = raw.MemoryUsedMiB;
                result.TemperatureC = raw.TemperatureC;
                result.PowerDrawW = raw.PowerDrawW;
                result.PowerLimitW = raw.PowerLimitW;
                result.MemoryUtilizationPercent = raw.MemoryUtilizationPercent;
                result.GraphicsClockMHz = raw.GraphicsClockMHz;
                result.MemoryClockMHz = raw.MemoryClockMHz;
            }
            else
            {
                result.UsagePercent = -1;
                result.MemoryTotalMiB = -1;
                result.MemoryUsedMiB = -1;
                result.TemperatureC = -1;
                result.PowerDrawW = -1;
                result.PowerLimitW = -1;
                result.MemoryUtilizationPercent = -1;
                result.GraphicsClockMHz = -1;
                result.MemoryClockMHz = -1;
            }

            if (!result.Present)
            {
                result.Health = HealthLevel.Offline;
                result.StatusText = "未连接";
                result.StatusDetail = "Windows 未检测到设备";
            }
            else if (!result.DriverStarted)
            {
                result.Health = HealthLevel.Error;
                result.StatusText = pnp.ProblemCode == 22 ? "已禁用" : "驱动异常";
            }
            else if (raw == null)
            {
                result.StatusText = "运行正常";
                result.StatusDetail = string.Equals(vendor, "NVIDIA", StringComparison.OrdinalIgnoreCase)
                    ? "nvidia-smi 未返回该设备的详细遥测"
                    : "Windows PnP 基础监视；该厂商未接入性能遥测";
                result.Health = string.Equals(vendor, "NVIDIA", StringComparison.OrdinalIgnoreCase)
                    ? HealthLevel.Warning
                    : HealthLevel.Healthy;
            }
            else
            {
                result.Health = HealthLevel.Healthy;
                result.StatusText = "运行正常";
                result.StatusDetail = string.Empty;
            }
            return result;
        }

        private static string Fallback(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "--" : value;
        }

        private static string ReadRecentDriverEvent()
        {
            try
            {
                using (EventLog log = new EventLog("System", Environment.MachineName))
                {
                    for (int index = log.Entries.Count - 1; index >= Math.Max(0, log.Entries.Count - 120); index--)
                    {
                        EventLogEntry entry = log.Entries[index];
                        if (!string.Equals(entry.Source, "nvlddmkm", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string message = string.IsNullOrWhiteSpace(entry.Message)
                            ? "nvlddmkm 事件 " + entry.InstanceId.ToString(CultureInfo.InvariantCulture) +
                              "（驱动未提供文本）"
                            : entry.Message.Replace("\r", " ").Replace("\n", " ").Trim();
                        if (message.Length > 180)
                        {
                            message = message.Substring(0, 177) + "...";
                        }
                        return entry.TimeGenerated.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                            "  " + message;
                    }
                }
            }
            catch
            {
            }
            return string.Empty;
        }
    }

    internal static class DrawingUtil
    {
        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(1, radius * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color HealthColor(HealthLevel level)
        {
            switch (level)
            {
                case HealthLevel.Healthy:
                    return Palette.Green;
                case HealthLevel.Warning:
                    return Palette.Amber;
                case HealthLevel.Error:
                    return Palette.Red;
                default:
                    return Palette.Dim;
            }
        }

        public static void DrawText(Graphics graphics, string text, Font font, Color color,
            Rectangle bounds, TextFormatFlags flags)
        {
            TextRenderer.DrawText(graphics, text ?? string.Empty, font, bounds, color, flags);
        }

        public static void DrawSection(Graphics graphics, Rectangle bounds, string title)
        {
            using (SolidBrush brush = new SolidBrush(Palette.Surface))
            using (Pen pen = new Pen(Palette.Border))
            using (GraphicsPath path = RoundedRect(bounds, 7))
            {
                graphics.FillPath(brush, path);
                graphics.DrawPath(pen, path);
            }
            using (Font titleFont = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    graphics,
                    title,
                    titleFont,
                    new Rectangle(bounds.Left + 18, bounds.Top + 12, bounds.Width - 36, 24),
                    Palette.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class HeaderView : Control
    {
        private string subtitle;
        private string statusText;
        private HealthLevel statusHealth;

        public FlowLayoutPanel ButtonPanel { get; private set; }

        public HeaderView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Palette.Background;
            subtitle = "自动发现并监视所选显卡";
            statusText = "正在初始化";
            statusHealth = HealthLevel.Offline;
            ButtonPanel = new FlowLayoutPanel();
            ButtonPanel.FlowDirection = FlowDirection.LeftToRight;
            ButtonPanel.WrapContents = false;
            ButtonPanel.AutoSize = false;
            ButtonPanel.Width = 440;
            ButtonPanel.Height = 38;
            ButtonPanel.BackColor = Color.Transparent;
            ButtonPanel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(ButtonPanel);
        }

        public void SetState(string newSubtitle, string newStatus, HealthLevel health)
        {
            subtitle = newSubtitle;
            statusText = newStatus;
            statusHealth = health;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ButtonPanel.Location = new Point(Math.Max(0, Width - ButtonPanel.Width), 14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Font title = new Font("Segoe UI Semibold", 18.5f, FontStyle.Bold))
            using (Font sub = new Font("Segoe UI", 9.5f, FontStyle.Regular))
            using (Font status = new Font("Segoe UI Semibold", 8.3f, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "GPU SENTINEL",
                    title,
                    new Rectangle(0, 8, 380, 34),
                    Palette.Text,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(
                    e.Graphics,
                    subtitle,
                    sub,
                    new Rectangle(2, 43, Math.Max(220, Width - ButtonPanel.Width - 30), 22),
                    Palette.Muted,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

                Color healthColor = DrawingUtil.HealthColor(statusHealth);
                Size size = TextRenderer.MeasureText(statusText, status);
                Rectangle pill = new Rectangle(
                    Width - ButtonPanel.Width - size.Width - 38,
                    11,
                    size.Width + 24,
                    24);
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(38, healthColor)))
                using (GraphicsPath path = DrawingUtil.RoundedRect(pill, 6))
                {
                    e.Graphics.FillPath(fill, path);
                }
                using (SolidBrush dot = new SolidBrush(healthColor))
                {
                    e.Graphics.FillEllipse(dot, pill.Left + 8, pill.Top + 8, 8, 8);
                }
                TextRenderer.DrawText(
                    e.Graphics,
                    statusText,
                    status,
                    new Rectangle(pill.Left + 21, pill.Top, pill.Width - 22, pill.Height),
                    healthColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class IconButton : Control
    {
        private bool hovered;
        private bool pressed;

        public string Glyph { get; set; }
        public string Label { get; set; }

        public IconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            Glyph = "\uE72C";
            Label = "刷新";
            Size = new Size(76, 34);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = hovered
                ? (pressed ? Palette.SurfaceRaised : Color.FromArgb(42, 47, 54))
                : Palette.Surface;
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen border = new Pen(hovered ? Color.FromArgb(78, 86, 96) : Palette.Border))
            using (GraphicsPath path = DrawingUtil.RoundedRect(bounds, 6))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(border, path);
            }
            using (Font icon = new Font("Segoe MDL2 Assets", 10.5f, FontStyle.Regular))
            using (Font text = new Font("Segoe UI", 9f, FontStyle.Regular))
            {
                TextRenderer.DrawText(
                    e.Graphics, Glyph, icon,
                    new Rectangle(9, 0, 22, Height),
                    Palette.Cyan,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(
                    e.Graphics, Label, text,
                    new Rectangle(32, 0, Width - 36, Height),
                    Palette.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.EndEllipsis);
            }
        }
    }

    internal sealed class DeviceCard : Control
    {
        public GpuReading Reading { get; set; }
        public Color Accent { get; set; }
        public string ShortRole { get; set; }

        public DeviceCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Reading = new GpuReading("GPU");
            Accent = Palette.Cyan;
            ShortRole = "GPU";
            BackColor = Palette.Background;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush fill = new SolidBrush(Palette.Surface))
            using (Pen border = new Pen(Palette.Border))
            using (GraphicsPath path = DrawingUtil.RoundedRect(bounds, 7))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }

            Color statusColor = DrawingUtil.HealthColor(Reading.Health);
            using (Font roleFont = new Font("Segoe UI Semibold", 12f, FontStyle.Bold))
            using (Font nameFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            using (Font statusFont = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold))
            using (Font metricLabel = new Font("Segoe UI", 8.2f, FontStyle.Regular))
            using (Font metricValue = new Font("Segoe UI Semibold", 13.5f, FontStyle.Bold))
            using (Font small = new Font("Segoe UI", 8f, FontStyle.Regular))
            using (Font smallValue = new Font("Segoe UI Semibold", 8.8f, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    e.Graphics, ShortRole, roleFont,
                    new Rectangle(18, 13, Width - 130, 26),
                    Palette.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(
                    e.Graphics, Reading.Name, nameFont,
                    new Rectangle(19, 38, Width - 130, 20),
                    Palette.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.EndEllipsis);

                Size statusSize = TextRenderer.MeasureText(Reading.StatusText, statusFont);
                Rectangle pill = new Rectangle(Width - statusSize.Width - 38, 17, statusSize.Width + 22, 23);
                using (SolidBrush statusFill = new SolidBrush(Color.FromArgb(38, statusColor)))
                using (GraphicsPath pillPath = DrawingUtil.RoundedRect(pill, 6))
                {
                    e.Graphics.FillPath(statusFill, pillPath);
                }
                TextRenderer.DrawText(
                    e.Graphics, Reading.StatusText, statusFont,
                    new Rectangle(pill.Left + 10, pill.Top, pill.Width - 10, pill.Height),
                    statusColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                DrawGauge(e.Graphics, new Rectangle(18, 72, 124, 124), Reading.UsagePercent, metricValue, metricLabel);

                int valueX = 152;
                int valueWidth = Math.Max(100, Width - valueX - 18);
                DrawMetric(e.Graphics, "显存", Formatters.Memory(Reading.MemoryUsedMiB, Reading.MemoryTotalMiB),
                    new Rectangle(valueX, 70, valueWidth, 44), metricLabel, smallValue);
                DrawMetric(e.Graphics, "温度", Formatters.Number(Reading.TemperatureC, " °C", 0),
                    new Rectangle(valueX, 113, valueWidth / 2, 42), metricLabel, smallValue);
                DrawMetric(e.Graphics, "功耗", Formatters.Number(Reading.PowerDrawW, " W", 1),
                    new Rectangle(valueX + valueWidth / 2, 113, valueWidth / 2, 42), metricLabel, smallValue);
                DrawMetric(e.Graphics, "核心", Formatters.Number(Reading.GraphicsClockMHz, " MHz", 0),
                    new Rectangle(valueX, 154, valueWidth / 2, 42), metricLabel, smallValue);
                DrawMetric(e.Graphics, "显存频率", Formatters.Number(Reading.MemoryClockMHz, " MHz", 0),
                    new Rectangle(valueX + valueWidth / 2, 154, valueWidth / 2, 42), metricLabel, smallValue);

                Rectangle memoryTrack = new Rectangle(18, 211, Width - 36, 8);
                using (SolidBrush track = new SolidBrush(Palette.SurfaceRaised))
                using (GraphicsPath trackPath = DrawingUtil.RoundedRect(memoryTrack, 4))
                {
                    e.Graphics.FillPath(track, trackPath);
                }
                double memoryPercent = Reading.MemoryUsagePercent;
                if (memoryPercent > 0)
                {
                    int width = Math.Max(4, (int)Math.Round(memoryTrack.Width * memoryPercent / 100.0));
                    Rectangle used = new Rectangle(memoryTrack.Left, memoryTrack.Top, width, memoryTrack.Height);
                    using (SolidBrush usedBrush = new SolidBrush(Accent))
                    using (GraphicsPath usedPath = DrawingUtil.RoundedRect(used, 4))
                    {
                        e.Graphics.FillPath(usedBrush, usedPath);
                    }
                }

                DrawDetail(e.Graphics, "驱动", Fallback(Reading.DriverVersion),
                    new Rectangle(18, 234, Width / 4 - 12, 36), small, smallValue);
                DrawDetail(e.Graphics, "模式", Fallback(Reading.Mode),
                    new Rectangle(18 + Width / 4, 234, Width / 4 - 12, 36), small, smallValue);
                DrawDetail(e.Graphics, "PCIe", Fallback(Reading.Pcie),
                    new Rectangle(18 + Width / 2, 234, Width / 4 - 12, 36), small, smallValue);
                DrawDetail(e.Graphics, "INF", Fallback(Reading.InfName),
                    new Rectangle(18 + Width * 3 / 4, 234, Width / 4 - 30, 36), small, smallValue);

            }
        }

        private void DrawGauge(Graphics graphics, Rectangle bounds, double value, Font valueFont, Font labelFont)
        {
            double percent = value < 0 ? 0 : Math.Max(0, Math.Min(100, value));
            using (Pen track = new Pen(Palette.SurfaceRaised, 10))
            using (Pen progress = new Pen(Accent, 10))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                progress.StartCap = LineCap.Round;
                progress.EndCap = LineCap.Round;
                Rectangle arcBounds = new Rectangle(bounds.Left + 7, bounds.Top + 7, bounds.Width - 14, bounds.Height - 14);
                graphics.DrawArc(track, arcBounds, 150, 240);
                if (percent > 0)
                {
                    graphics.DrawArc(progress, arcBounds, 150, (float)(240 * percent / 100.0));
                }
            }
            string valueText = value < 0 ? "--" : Math.Round(percent).ToString("F0", CultureInfo.InvariantCulture) + "%";
            TextRenderer.DrawText(
                graphics, valueText, valueFont,
                new Rectangle(bounds.Left, bounds.Top + 37, bounds.Width, 38),
                Palette.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(
                graphics, "GPU 利用率", labelFont,
                new Rectangle(bounds.Left, bounds.Top + 72, bounds.Width, 24),
                Palette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static void DrawMetric(Graphics graphics, string label, string value, Rectangle bounds,
            Font labelFont, Font valueFont)
        {
            TextRenderer.DrawText(
                graphics, label, labelFont,
                new Rectangle(bounds.Left, bounds.Top, bounds.Width, 17),
                Palette.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(
                graphics, value, valueFont,
                new Rectangle(bounds.Left, bounds.Top + 17, bounds.Width, 24),
                Palette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis);
        }

        private static void DrawDetail(Graphics graphics, string label, string value, Rectangle bounds,
            Font labelFont, Font valueFont)
        {
            TextRenderer.DrawText(
                graphics, label, labelFont,
                new Rectangle(bounds.Left, bounds.Top, bounds.Width, 15),
                Palette.Dim,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(
                graphics, value, valueFont,
                new Rectangle(bounds.Left, bounds.Top + 15, bounds.Width, 19),
                Palette.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        private static string Fallback(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "--" : value;
        }
    }

    internal sealed class TrendChart : Control
    {
        private IList<HistorySample> samples = new List<HistorySample>();
        private IList<GpuReading> devices = new List<GpuReading>();
        private static readonly Color[] SeriesColors =
        {
            Palette.Cyan, Palette.Green, Palette.Amber, Palette.Blue, Palette.Red
        };

        public TrendChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Palette.Background;
        }

        public void SetSamples(IList<HistorySample> data, IList<GpuReading> selectedDevices)
        {
            samples = data;
            devices = selectedDevices ?? new List<GpuReading>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            DrawingUtil.DrawSection(e.Graphics, bounds, "所选显卡利用率趋势");
            Rectangle plot = new Rectangle(48, 50, Math.Max(20, Width - 68), Math.Max(20, Height - 80));

            using (Font small = new Font("Segoe UI", 8f, FontStyle.Regular))
            using (Pen grid = new Pen(Palette.Grid))
            {
                grid.DashStyle = DashStyle.Dot;
                for (int i = 0; i <= 4; i++)
                {
                    int y = plot.Top + plot.Height * i / 4;
                    e.Graphics.DrawLine(grid, plot.Left, y, plot.Right, y);
                    int percent = 100 - i * 25;
                    TextRenderer.DrawText(
                        e.Graphics, percent.ToString(CultureInfo.InvariantCulture), small,
                        new Rectangle(7, y - 8, 35, 16), Palette.Dim,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                e.Graphics.DrawLine(new Pen(Palette.Border), plot.Left, plot.Bottom, plot.Right, plot.Bottom);
            }

            DrawLegend(e.Graphics, new Rectangle(Math.Max(240, Width - 330), 12, 320, 32));
            if (samples.Count >= 2)
            {
                for (int index = 0; index < Math.Min(devices.Count, SeriesColors.Length); index++)
                {
                    DrawSeries(e.Graphics, plot, SeriesColors[index], devices[index].DeviceKey, samples.Count);
                }
            }
            else
            {
                using (Font empty = new Font("Segoe UI", 9f, FontStyle.Regular))
                {
                    TextRenderer.DrawText(
                        e.Graphics, "等待采样", empty, plot, Palette.Dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        private void DrawSeries(Graphics graphics, Rectangle plot, Color color, string deviceKey, int count)
        {
            using (Pen pen = new Pen(color, 2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                PointF previous = PointF.Empty;
                for (int i = 0; i < count; i++)
                {
                    double value;
                    if (!samples[i].Usage.TryGetValue(deviceKey, out value))
                    {
                        value = -1;
                    }
                    if (value < 0)
                    {
                        previous = PointF.Empty;
                        continue;
                    }
                    float x = plot.Left + (count == 1 ? 0 : plot.Width * (float)i / (count - 1));
                    float y = plot.Bottom - plot.Height * (float)Math.Max(0, Math.Min(100, value)) / 100f;
                    PointF current = new PointF(x, y);
                    if (!previous.IsEmpty)
                    {
                        graphics.DrawLine(pen, previous, current);
                    }
                    previous = current;
                }
            }
        }

        private void DrawLegend(Graphics graphics, Rectangle bounds)
        {
            using (Font font = new Font("Segoe UI", 8f, FontStyle.Regular))
            {
                int shown = Math.Min(devices.Count, SeriesColors.Length);
                for (int index = 0; index < shown; index++)
                {
                    int column = index % 2;
                    int row = index / 2;
                    int left = bounds.Left + column * 155;
                    int top = bounds.Top + row * 15;
                    using (SolidBrush dot = new SolidBrush(SeriesColors[index]))
                    {
                        graphics.FillEllipse(dot, left, top + 5, 7, 7);
                    }
                    TextRenderer.DrawText(
                        graphics, LabelFor(devices[index]), font,
                        new Rectangle(left + 12, top - 3, 138, 22), Palette.Muted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private static string LabelFor(GpuReading reading)
        {
            string name = reading == null ? "GPU" : reading.Name;
            if (name.Length <= 22)
            {
                return name;
            }
            return name.Substring(0, 20) + "...";
        }
    }

    internal sealed class EventListView : Control
    {
        private IList<LogItem> items = new List<LogItem>();

        public EventListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Palette.Background;
        }

        public void SetItems(IList<LogItem> data)
        {
            items = data;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            DrawingUtil.DrawSection(e.Graphics, bounds, "运行事件");
            using (Font timeFont = new Font("Consolas", 7.8f, FontStyle.Regular))
            using (Font textFont = new Font("Segoe UI", 8.2f, FontStyle.Regular))
            {
                int y = 48;
                int count = Math.Min(5, items.Count);
                for (int offset = 0; offset < count; offset++)
                {
                    LogItem item = items[items.Count - 1 - offset];
                    Color color = DrawingUtil.HealthColor(item.Level);
                    using (SolidBrush dot = new SolidBrush(color))
                    {
                        e.Graphics.FillEllipse(dot, 18, y + 6, 6, 6);
                    }
                    TextRenderer.DrawText(
                        e.Graphics, Formatters.Time(item.Timestamp), timeFont,
                        new Rectangle(31, y, 82, 18), Palette.Dim,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(
                        e.Graphics, item.Message, textFont,
                        new Rectangle(119, y - 1, Width - 135, 42),
                        Palette.Muted,
                        TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPadding);
                    y += 49;
                }
                if (items.Count == 0)
                {
                    TextRenderer.DrawText(
                        e.Graphics, "暂无事件", textFont, bounds, Palette.Dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }

    internal sealed class DeviceSelectionForm : Form
    {
        private readonly CheckedListBox devicesList;
        private readonly List<PnpReading> devices;

        public List<string> SelectedIds { get; private set; }

        public DeviceSelectionForm(IEnumerable<PnpReading> availableDevices, ISet<string> selectedIds)
        {
            devices = (availableDevices ?? Enumerable.Empty<PnpReading>()).ToList();
            SelectedIds = new List<string>();

            Text = "选择要监视的显卡";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(650, 430);
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);

            Label hint = new Label();
            hint.Text = "勾选需要同时监视的核显、独显或计算卡。未勾选设备不会采集。";
            hint.ForeColor = Palette.Muted;
            hint.AutoSize = false;
            hint.Location = new Point(18, 14);
            hint.Size = new Size(614, 32);

            devicesList = new CheckedListBox();
            devicesList.Location = new Point(18, 50);
            devicesList.Size = new Size(614, 300);
            devicesList.CheckOnClick = true;
            devicesList.BackColor = Palette.Surface;
            devicesList.ForeColor = Palette.Text;
            devicesList.BorderStyle = BorderStyle.FixedSingle;
            devicesList.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            devicesList.HorizontalScrollbar = true;
            devicesList.HorizontalExtent = 900;

            foreach (PnpReading device in devices)
            {
                string state = device.ProblemCode == 0 ? "正常" : device.ProblemText;
                string driver = string.IsNullOrWhiteSpace(device.DriverVersion) ? "--" : device.DriverVersion;
                string label = "[" + device.Vendor + "]  " + device.Name + "   · " + state + "   · " + driver;
                int index = devicesList.Items.Add(label);
                bool shouldCheck = selectedIds == null || selectedIds.Count == 0 || selectedIds.Contains(device.InstanceId);
                devicesList.SetItemChecked(index, shouldCheck);
            }

            Button selectAll = new Button();
            selectAll.Text = "全选";
            selectAll.Location = new Point(18, 365);
            selectAll.Size = new Size(88, 32);
            selectAll.FlatStyle = FlatStyle.Flat;
            selectAll.Click += delegate
            {
                for (int index = 0; index < devicesList.Items.Count; index++)
                {
                    devicesList.SetItemChecked(index, true);
                }
            };

            Button clear = new Button();
            clear.Text = "清空";
            clear.Location = new Point(114, 365);
            clear.Size = new Size(88, 32);
            clear.FlatStyle = FlatStyle.Flat;
            clear.Click += delegate
            {
                for (int index = 0; index < devicesList.Items.Count; index++)
                {
                    devicesList.SetItemChecked(index, false);
                }
            };

            Button ok = new Button();
            ok.Text = "确定";
            ok.Location = new Point(448, 365);
            ok.Size = new Size(88, 32);
            ok.DialogResult = DialogResult.OK;
            ok.Click += delegate(object sender, EventArgs args)
            {
                List<string> chosen = new List<string>();
                for (int index = 0; index < devicesList.Items.Count; index++)
                {
                    if (devicesList.GetItemChecked(index))
                    {
                        chosen.Add(devices[index].InstanceId);
                    }
                }
                if (chosen.Count == 0)
                {
                    DialogResult = DialogResult.None;
                    MessageBox.Show(this, "至少选择一张显卡。", "GPU Sentinel",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                SelectedIds = chosen;
            };

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(544, 365);
            cancel.Size = new Size(88, 32);
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(hint);
            Controls.Add(devicesList);
            Controls.Add(selectAll);
            Controls.Add(clear);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly AppConfig config;
        private readonly MonitorService monitorService;
        private readonly HeaderView header;
        private readonly FlowLayoutPanel cardsPanel;
        private readonly Dictionary<string, DeviceCard> deviceCards;
        private readonly Dictionary<string, HealthLevel> previousDeviceHealth;
        private readonly HashSet<string> selectedDevices;
        private List<PnpReading> availableDevices;
        private readonly bool openDeviceSelectorOnShow;
        private readonly TrendChart trendChart;
        private readonly EventListView eventList;
        private readonly ToolStripStatusLabel statusLabel;
        private readonly ToolStripStatusLabel refreshedLabel;
        private readonly ToolStripStatusLabel detailLabel;
        private readonly NotifyIcon trayIcon;
        private readonly ToolTip toolTip;
        private readonly WinFormsTimer timer;
        private readonly List<HistorySample> history;
        private readonly List<LogItem> events;
        private readonly string logDirectory;
        private readonly object csvLock = new object();
        private bool refreshing;
        private bool allowExit;
        private DateTime previousAlertTime;
        private string lastSmiError;

        public MainForm() : this(false)
        {
        }

        public MainForm(bool showDeviceSelector)
        {
            openDeviceSelectorOnShow = showDeviceSelector;
            config = AppConfig.Load();
            monitorService = new MonitorService();
            history = new List<HistorySample>();
            events = new List<LogItem>();
            deviceCards = new Dictionary<string, DeviceCard>(StringComparer.OrdinalIgnoreCase);
            previousDeviceHealth = new Dictionary<string, HealthLevel>(StringComparer.OrdinalIgnoreCase);
            selectedDevices = new HashSet<string>(config.SelectedDevices, StringComparer.OrdinalIgnoreCase);
            availableDevices = monitorService.GetAvailableDevices();
            if (selectedDevices.Count == 0)
            {
                foreach (PnpReading device in availableDevices)
                {
                    selectedDevices.Add(device.InstanceId);
                }
                if (selectedDevices.Count > 0)
                {
                    config.SaveSelectedDevices(selectedDevices);
                }
            }
            logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);

            Text = "GPU Sentinel";
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 720);
            Size = new Size(1260, 850);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            Icon = CreateWindowIcon();
            DoubleBuffered = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Palette.Background;
            root.Padding = new Padding(18, 12, 18, 8);
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 310));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            Controls.Add(root);

            header = new HeaderView();
            header.Dock = DockStyle.Fill;
            header.Margin = new Padding(0);
            root.Controls.Add(header, 0, 0);

            IconButton refreshButton = CreateButton("\uE72C", "刷新");
            IconButton devicesButton = CreateButton("\uE7F4", "显卡");
            IconButton llamaButton = CreateButton("\uE774", "LLM");
            IconButton logsButton = CreateButton("\uE8A5", "日志");
            IconButton configButton = CreateButton("\uE713", "配置");
            refreshButton.Click += delegate { RefreshData(true); };
            devicesButton.Click += delegate { OpenDeviceSelection(); };
            llamaButton.Click += delegate { OpenLlama(); };
            logsButton.Click += delegate { OpenLogs(); };
            configButton.Click += delegate { OpenConfig(); };
            header.ButtonPanel.Controls.Add(refreshButton);
            header.ButtonPanel.Controls.Add(devicesButton);
            header.ButtonPanel.Controls.Add(llamaButton);
            header.ButtonPanel.Controls.Add(logsButton);
            header.ButtonPanel.Controls.Add(configButton);

            toolTip = new ToolTip();
            toolTip.SetToolTip(refreshButton, "立即刷新所选显卡状态");
            toolTip.SetToolTip(devicesButton, "选择要监视的核显、独显或计算卡");
            toolTip.SetToolTip(llamaButton, "打开本机 llama.cpp Web UI");
            toolTip.SetToolTip(logsButton, "打开监控日志目录");
            toolTip.SetToolTip(configButton, "编辑监控器配置");

            cardsPanel = new FlowLayoutPanel();
            cardsPanel.Dock = DockStyle.Fill;
            cardsPanel.Margin = new Padding(0, 0, 0, 10);
            cardsPanel.Padding = new Padding(0, 0, 0, 8);
            cardsPanel.FlowDirection = FlowDirection.LeftToRight;
            cardsPanel.WrapContents = false;
            cardsPanel.AutoScroll = true;
            cardsPanel.BackColor = Palette.Background;
            cardsPanel.Resize += delegate { LayoutDeviceCards(); };
            root.Controls.Add(cardsPanel, 0, 1);

            TableLayoutPanel lower = new TableLayoutPanel();
            lower.Dock = DockStyle.Fill;
            lower.Margin = new Padding(0);
            lower.ColumnCount = 2;
            lower.RowCount = 1;
            lower.BackColor = Palette.Background;
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            root.Controls.Add(lower, 0, 2);

            trendChart = new TrendChart();
            trendChart.Dock = DockStyle.Fill;
            trendChart.Margin = new Padding(0, 0, 7, 0);
            lower.Controls.Add(trendChart, 0, 0);

            eventList = new EventListView();
            eventList.Dock = DockStyle.Fill;
            eventList.Margin = new Padding(7, 0, 0, 0);
            lower.Controls.Add(eventList, 1, 0);

            RebuildCards(availableDevices);

            StatusStrip statusStrip = new StatusStrip();
            statusStrip.Dock = DockStyle.Fill;
            statusStrip.SizingGrip = false;
            statusStrip.BackColor = Palette.Background;
            statusStrip.ForeColor = Palette.Muted;
            statusStrip.Padding = new Padding(0);
            statusLabel = new ToolStripStatusLabel();
            statusLabel.Text = "正在初始化";
            statusLabel.ForeColor = Palette.Muted;
            refreshedLabel = new ToolStripStatusLabel();
            refreshedLabel.Text = "尚未刷新";
            refreshedLabel.ForeColor = Palette.Dim;
            refreshedLabel.Spring = true;
            refreshedLabel.TextAlign = ContentAlignment.MiddleRight;
            detailLabel = new ToolStripStatusLabel();
            detailLabel.Text = "自动刷新 " + config.RefreshSeconds + "s";
            detailLabel.ForeColor = Palette.Dim;
            statusStrip.Items.Add(statusLabel);
            statusStrip.Items.Add(refreshedLabel);
            statusStrip.Items.Add(detailLabel);
            root.Controls.Add(statusStrip, 0, 3);

            trayIcon = new NotifyIcon();
            trayIcon.Icon = CreateTrayIcon(HealthLevel.Offline);
            trayIcon.Text = "GPU Sentinel";
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate
            {
                ShowFromTray();
            };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("打开监控器", null, delegate { ShowFromTray(); });
            menu.Items.Add("立即刷新", null, delegate { RefreshData(true); });
            menu.Items.Add("打开 llama", null, delegate { OpenLlama(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApplication(); });
            trayIcon.ContextMenuStrip = menu;

            timer = new WinFormsTimer();
            timer.Interval = Math.Max(1, config.RefreshSeconds) * 1000;
            timer.Tick += delegate { RefreshData(false); };
            Shown += delegate
            {
                AddEvent(HealthLevel.Healthy, "监控器已启动，将监视所选显卡");
                RefreshData(true);
                timer.Start();
                if (openDeviceSelectorOnShow)
                {
                    BeginInvoke(new Action(OpenDeviceSelection));
                }
                if (config.StartMinimized)
                {
                    BeginInvoke(new Action(HideToTray));
                }
            };
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    HideToTray();
                }
            };
            FormClosing += OnFormClosing;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int enabled = 1;
                DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
                DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
            }
            catch
            {
            }
        }

        private IconButton CreateButton(string glyph, string label)
        {
            IconButton button = new IconButton();
            button.Glyph = glyph;
            button.Label = label;
            button.AccessibleName = label;
            button.AccessibleRole = AccessibleRole.PushButton;
            button.Margin = new Padding(6, 0, 0, 0);
            return button;
        }

        private void OpenDeviceSelection()
        {
            availableDevices = monitorService.GetAvailableDevices();
            if (availableDevices.Count == 0)
            {
                MessageBox.Show(this, "未发现 PCI 显示适配器。", "GPU Sentinel",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (DeviceSelectionForm dialog = new DeviceSelectionForm(availableDevices, selectedDevices))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                selectedDevices.Clear();
                foreach (string id in dialog.SelectedIds)
                {
                    selectedDevices.Add(id);
                }
                config.SaveSelectedDevices(selectedDevices);
            }

            monitorService.InvalidatePnpCache();
            RebuildCards(availableDevices);
            AddEvent(HealthLevel.Healthy,
                "监视设备已更新为 " + selectedDevices.Count.ToString(CultureInfo.InvariantCulture) + " 张显卡");
            RefreshData(true);
        }

        private void RebuildCards(IEnumerable<PnpReading> devices)
        {
            cardsPanel.SuspendLayout();
            cardsPanel.Controls.Clear();
            deviceCards.Clear();
            previousDeviceHealth.Clear();

            foreach (PnpReading device in devices ?? Enumerable.Empty<PnpReading>())
            {
                if (!selectedDevices.Contains(device.InstanceId))
                {
                    continue;
                }

                DeviceCard card = new DeviceCard();
                card.Accent = VendorColor(device.Vendor);
                card.ShortRole = VendorRole(device.Vendor);
                card.Margin = new Padding(0, 0, 14, 0);
                card.Cursor = Cursors.Hand;
                card.AccessibleName = device.Name;
                card.Click += delegate { OpenDeviceSelection(); };
                card.Reading = new GpuReading(VendorRole(device.Vendor))
                {
                    DeviceKey = device.InstanceId,
                    Vendor = device.Vendor,
                    Name = device.Name,
                    DriverVersion = string.IsNullOrWhiteSpace(device.DriverVersion) ? "--" : device.DriverVersion,
                    InfName = string.IsNullOrWhiteSpace(device.InfName) ? "--" : device.InfName,
                    ProblemCode = device.ProblemCode,
                    Present = device.Present
                };
                cardsPanel.Controls.Add(card);
                deviceCards[device.InstanceId] = card;
            }

            cardsPanel.ResumeLayout();
            LayoutDeviceCards();
        }

        private void LayoutDeviceCards()
        {
            int count = cardsPanel.Controls.Count;
            if (count == 0)
            {
                return;
            }
            int columns = Math.Min(Math.Max(1, count), 3);
            int viewport = Math.Max(cardsPanel.Width, ClientSize.Width - 56);
            int available = Math.Max(320, viewport - 24);
            int width = Math.Max(280, (available - (columns - 1) * 14) / columns);
            int height = Math.Max(250, cardsPanel.ClientSize.Height - 24);
            foreach (Control control in cardsPanel.Controls)
            {
                control.Size = new Size(width, height);
            }
        }

        private static Color VendorColor(string vendor)
        {
            if (string.Equals(vendor, "NVIDIA", StringComparison.OrdinalIgnoreCase)) return Palette.Green;
            if (string.Equals(vendor, "Intel", StringComparison.OrdinalIgnoreCase)) return Palette.Cyan;
            if (string.Equals(vendor, "AMD", StringComparison.OrdinalIgnoreCase)) return Palette.Amber;
            return Palette.Blue;
        }

        private static string VendorRole(string vendor)
        {
            if (string.Equals(vendor, "NVIDIA", StringComparison.OrdinalIgnoreCase)) return "NVIDIA";
            if (string.Equals(vendor, "Intel", StringComparison.OrdinalIgnoreCase)) return "INTEL";
            if (string.Equals(vendor, "AMD", StringComparison.OrdinalIgnoreCase)) return "AMD";
            return "GPU";
        }

        private void RefreshData(bool manual)
        {
            if (refreshing)
            {
                return;
            }
            refreshing = true;
            statusLabel.Text = "正在刷新";
            Task.Factory.StartNew(() => monitorService.Capture(config)).ContinueWith(delegate(Task<MonitorSnapshot> task)
            {
                if (IsDisposed)
                {
                    return;
                }
                try
                {
                    if (task.IsFaulted)
                    {
                        BeginInvoke(new Action(delegate
                        {
                            refreshing = false;
                            statusLabel.Text = "刷新失败";
                            detailLabel.Text = task.Exception != null
                                ? task.Exception.GetBaseException().Message
                                : "未知错误";
                        }));
                        return;
                    }
                    MonitorSnapshot snapshot = task.Result;
                    BeginInvoke(new Action(delegate { ApplySnapshot(snapshot); }));
                }
                catch (Exception exception)
                {
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            refreshing = false;
                            statusLabel.Text = "刷新失败";
                            detailLabel.Text = exception.GetBaseException().Message;
                        }));
                    }
                    catch
                    {
                    }
                }
            }, TaskScheduler.Default);
        }

        private void ApplySnapshot(MonitorSnapshot snapshot)
        {
            refreshing = false;
            List<GpuReading> readings = snapshot.Devices.ToList();
            foreach (GpuReading reading in readings)
            {
                DeviceCard card;
                if (!deviceCards.TryGetValue(reading.DeviceKey, out card))
                {
                    card = new DeviceCard();
                    card.Margin = new Padding(0, 0, 14, 0);
                    card.Cursor = Cursors.Hand;
                    card.Click += delegate { OpenDeviceSelection(); };
                    cardsPanel.Controls.Add(card);
                    deviceCards[reading.DeviceKey] = card;
                }
                card.Accent = VendorColor(reading.Vendor);
                card.ShortRole = VendorRole(reading.Vendor);
                card.Reading = reading;
                card.AccessibleName = reading.Name;
                card.AccessibleRole = AccessibleRole.Pane;
                card.Invalidate();
                toolTip.SetToolTip(card, BuildCardTooltip(reading));
            }
            LayoutDeviceCards();

            header.SetState(
                readings.Count.ToString(CultureInfo.InvariantCulture) + " 张显卡  ·  " +
                snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                snapshot.OverallText,
                snapshot.OverallHealth);
            trayIcon.Icon = CreateTrayIcon(snapshot.OverallHealth);
            trayIcon.Text = TrimBalloonText("GPU Sentinel - " + snapshot.OverallText);

            HistorySample sample = new HistorySample();
            sample.Timestamp = snapshot.Timestamp;
            foreach (GpuReading reading in readings)
            {
                sample.Usage[reading.DeviceKey] = reading.UsagePercent;
                sample.Temperature[reading.DeviceKey] = reading.TemperatureC;
            }
            history.Add(sample);
            while (history.Count > config.HistoryPoints)
            {
                history.RemoveAt(0);
            }
            trendChart.SetSamples(new List<HistorySample>(history), readings);
            eventList.SetItems(new List<LogItem>(events));

            statusLabel.Text = snapshot.OverallText;
            statusLabel.ForeColor = DrawingUtil.HealthColor(snapshot.OverallHealth);
            refreshedLabel.Text = "最后刷新 " + Formatters.Time(snapshot.Timestamp);
            detailLabel.Text = "监视 " + readings.Count.ToString(CultureInfo.InvariantCulture) +
                " 张 · 自动刷新 " + config.RefreshSeconds + "s";
            if (!string.IsNullOrWhiteSpace(snapshot.RawError) &&
                !string.Equals(snapshot.RawError, lastSmiError, StringComparison.Ordinal))
            {
                AddEvent(HealthLevel.Warning, "nvidia-smi: " + Compact(snapshot.RawError, 150));
                lastSmiError = snapshot.RawError;
            }

            foreach (GpuReading reading in readings)
            {
                HealthLevel previous;
                bool hadPrevious = previousDeviceHealth.TryGetValue(reading.DeviceKey, out previous);
                if (!hadPrevious || previous != reading.Health)
                {
                    AddEvent(reading.Health,
                        reading.Name + " · " + reading.StatusText +
                        (string.IsNullOrWhiteSpace(reading.StatusDetail) ? string.Empty : " · " + reading.StatusDetail));
                    if (hadPrevious && reading.Health != HealthLevel.Healthy &&
                        previous == HealthLevel.Healthy &&
                        (DateTime.Now - previousAlertTime).TotalMinutes > 2)
                    {
                        trayIcon.ShowBalloonTip(
                            5000,
                            reading.Name + " 状态异常",
                            reading.StatusText + " · " + reading.StatusDetail,
                            ToolTipIcon.Warning);
                        previousAlertTime = DateTime.Now;
                    }
                    previousDeviceHealth[reading.DeviceKey] = reading.Health;
                }

                if (reading.Health == HealthLevel.Healthy &&
                    config.AlertTemperature > 0 &&
                    reading.TemperatureC >= config.AlertTemperature &&
                    (DateTime.Now - previousAlertTime).TotalMinutes > 5)
                {
                    AddEvent(HealthLevel.Warning, reading.Name + " 温度达到 " +
                        Formatters.Number(reading.TemperatureC, " °C", 0));
                    trayIcon.ShowBalloonTip(5000, reading.Name + " 温度过高",
                        "当前温度 " + Formatters.Number(reading.TemperatureC, " °C", 0),
                        ToolTipIcon.Warning);
                    previousAlertTime = DateTime.Now;
                }
            }

            WriteCsv(snapshot);
        }

        private static string BuildCardTooltip(GpuReading reading)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}\r\n状态：{1}\r\n驱动：{2}\r\n模式：{3}\r\nPCI：{4}\r\nUUID：{5}\r\nECC：{6}\r\n详情：{7}",
                reading.Name,
                reading.StatusText,
                reading.DriverVersion,
                reading.Mode,
                reading.PciBus,
                reading.Uuid,
                reading.Ecc,
                reading.StatusDetail);
        }

        private void AddEvent(HealthLevel level, string message)
        {
            events.Add(new LogItem
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            });
            while (events.Count > 100)
            {
                events.RemoveAt(0);
            }
            eventList.SetItems(new List<LogItem>(events));
        }

        private void WriteCsv(MonitorSnapshot snapshot)
        {
            try
            {
                string file = Path.Combine(logDirectory,
                    "gpu-" + snapshot.Timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");
                lock (csvLock)
                {
                    bool writeHeader = !File.Exists(file);
                    using (StreamWriter writer = new StreamWriter(file, true, new UTF8Encoding(true)))
                    {
                        if (writeHeader)
                        {
                            writer.WriteLine(
                                "timestamp,role,name,status,health,usage_pct,memory_used_mib,memory_total_mib," +
                                "temperature_c,power_w,pstate,mode,driver,pcie,inf,problem");
                        }
                        foreach (GpuReading reading in snapshot.Devices)
                        {
                            WriteCsvGpu(writer, snapshot.Timestamp, reading);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void WriteCsvGpu(StreamWriter writer, DateTime timestamp, GpuReading reading)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(timestamp.ToString("O", CultureInfo.InvariantCulture)),
                Csv(reading.Role),
                Csv(reading.Name),
                Csv(reading.StatusText),
                Csv(reading.Health.ToString()),
                Csv(NumberOrEmpty(reading.UsagePercent)),
                Csv(NumberOrEmpty(reading.MemoryUsedMiB)),
                Csv(NumberOrEmpty(reading.MemoryTotalMiB)),
                Csv(NumberOrEmpty(reading.TemperatureC)),
                Csv(NumberOrEmpty(reading.PowerDrawW)),
                Csv(reading.PState),
                Csv(reading.Mode),
                Csv(reading.DriverVersion),
                Csv(reading.PciBus),
                Csv(reading.InfName),
                Csv(reading.ProblemCode.ToString(CultureInfo.InvariantCulture))
            }));
        }

        private static string NumberOrEmpty(double value)
        {
            return value < 0
                ? string.Empty
                : value.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static string Csv(string value)
        {
            string text = value ?? string.Empty;
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }
            return text;
        }

        private void OpenLlama()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = config.LlamaUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "无法打开 llama", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenLogs()
        {
            try
            {
                Process.Start("explorer.exe", "\"" + logDirectory + "\"");
            }
            catch
            {
            }
        }

        private void OpenConfig()
        {
            try
            {
                Process.Start("notepad.exe", "\"" + config.ConfigPath + "\"");
            }
            catch
            {
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void HideToTray()
        {
            Hide();
            trayIcon.Visible = true;
        }

        private void ExitApplication()
        {
            allowExit = true;
            Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            timer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
        }

        private static Icon CreateWindowIcon()
        {
            using (Bitmap bitmap = new Bitmap(64, 64))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush background = new SolidBrush(Color.FromArgb(20, 23, 27)))
            using (SolidBrush accent = new SolidBrush(Palette.Cyan))
            using (Font font = new Font("Segoe UI Semibold", 30f, FontStyle.Bold))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillRectangle(background, 0, 0, 64, 64);
                graphics.FillRectangle(accent, 10, 14, 8, 36);
                graphics.FillRectangle(accent, 25, 24, 8, 26);
                graphics.FillRectangle(accent, 40, 8, 8, 42);
                TextRenderer.DrawText(
                    graphics, "V", font, new Rectangle(42, 39, 20, 20),
                    Palette.Text, TextFormatFlags.NoPadding);
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return Icon.FromHandle(handle).Clone() as Icon;
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static Icon CreateTrayIcon(HealthLevel health)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush background = new SolidBrush(Color.FromArgb(20, 23, 27)))
            using (SolidBrush status = new SolidBrush(DrawingUtil.HealthColor(health)))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillEllipse(background, 0, 0, 32, 32);
                graphics.FillRectangle(status, 8, 10, 4, 12);
                graphics.FillRectangle(status, 14, 15, 4, 7);
                graphics.FillRectangle(status, 20, 7, 4, 15);
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return Icon.FromHandle(handle).Clone() as Icon;
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static string Compact(string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }
            string value = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length <= max ? value : value.Substring(0, Math.Max(0, max - 3)) + "...";
        }

        private static string TrimBalloonText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "GPU Sentinel";
            }
            return text.Length <= 63 ? text : text.Substring(0, 60) + "...";
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int attributeValue,
            int attributeSize);
    }
}
