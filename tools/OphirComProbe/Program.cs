using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace LaserEnergyMonitor.OphirComProbe
{
    internal static class Program
    {
        private const string ProgId = "OphirLMMeasurement.CoLMMeasurement";

        [STAThread]
        private static int Main(string[] args)
        {
            ProbeOptions options;
            if (!ProbeOptions.TryParse(args, out options))
            {
                ProbeOptions.PrintUsage();
                return 2;
            }

            ProbeReport report = new ProbeReport();
            int exitCode = 1;
            try
            {
                exitCode = new ProbeRunner(options, report).Run();
            }
            catch (Exception ex)
            {
                report.AppendLine("Probe failed.");
                report.AppendLine(BuildExceptionDetails(ex));
                exitCode = 3;
            }
            finally
            {
                string text = report.ToString().TrimEnd();
                Console.WriteLine(text);
                string path = WriteReport(text);
                Console.WriteLine();
                Console.WriteLine("Report file: " + path);
            }

            return exitCode;
        }

        private static string WriteReport(string report)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string directory = Path.Combine(baseDirectory, "output", "ophir-com-probe");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(
                directory,
                "ophir-com-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");
            File.WriteAllText(path, report, Encoding.UTF8);
            return path;
        }

        private static string BuildExceptionDetails(Exception ex)
        {
            StringBuilder builder = new StringBuilder();
            Exception current = ex;
            int depth = 0;
            while (current != null && depth < 8)
            {
                if (depth > 0)
                {
                    builder.Append(" -> ");
                }

                builder.Append(current.GetType().FullName);
                builder.Append(": ");
                builder.Append(current.Message);

                COMException comException = current as COMException;
                if (comException != null)
                {
                    builder.Append(" (HRESULT=0x");
                    builder.Append(comException.ErrorCode.ToString("X8", CultureInfo.InvariantCulture));
                    builder.Append(")");
                }

                current = current.InnerException;
                depth++;
            }

            return builder.ToString();
        }
    }

    internal sealed class ProbeRunner
    {
        private readonly ProbeOptions _options;
        private readonly ProbeReport _report;

        public ProbeRunner(ProbeOptions options, ProbeReport report)
        {
            _options = options;
            _report = report;
        }

        public int Run()
        {
            _report.AppendLine("Ophir COM Probe generated at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            _report.AppendLine("Reference flow: OphirLMMeasurement.CoLMMeasurement, ScanUSB, OpenUSBDevice, IsSensorExists, StartStream, GetData, StopStream, Close");
            _report.AppendLine("Process bitness: " + (Environment.Is64BitProcess ? "x64" : "x86"));
            _report.AppendLine("Apartment: " + Thread.CurrentThread.GetApartmentState());
            _report.AppendLine("Duration: " + _options.Duration);
            _report.AppendLine("Pulse-triggered note: Ophir pulsed sensors may emit no samples until a laser pulse reaches the sensor.");
            _report.AppendLine("During this probe, fire at least one safe test pulse if live sample validation is required.");
            _report.AppendLine("Configured serial: " + (string.IsNullOrWhiteSpace(_options.SerialNumber) ? "auto-first-detected" : _options.SerialNumber));
            _report.AppendLine("Reset retry enabled: " + FormatBool(_options.ResetRetryEnabled));
            _report.AppendLine();

            Type runtimeType = Type.GetTypeFromProgID(ProgramConstants.ProgId, false);
            if (runtimeType == null)
            {
                _report.Step("COM registration", "FAIL", "ProgID was not found: " + ProgramConstants.ProgId);
                return 3;
            }

            _report.Step("COM registration", "PASS", "ProgID detected: " + ProgramConstants.ProgId);
            AppendComPackageDiagnostic(runtimeType);

            object comObject = null;
            int handle = 0;
            int channel = -1;
            bool streaming = false;
            try
            {
                comObject = Activator.CreateInstance(runtimeType);
                _report.Step("COM activation", "PASS", "COM type: " + runtimeType.FullName);
                AppendVersion(comObject, "GetVersion", "Runtime version");
                AppendVersion(comObject, "GetDriverVersion", "Driver version");

                string[] serials = ScanUsb(comObject, "Initial ScanUSB");
                if (serials.Length == 0 && _options.ResetRetryEnabled)
                {
                    _report.AppendLine("Initial ScanUSB returned zero devices. Running CloseAll + ResetAllDevices retry.");
                    TryInvoke(comObject, "CloseAll");
                    TryInvoke(comObject, "ResetAllDevices");
                    Thread.Sleep(_options.ResetDelay);
                    serials = ScanUsb(comObject, "ScanUSB after reset");
                }

                if (serials.Length == 0)
                {
                    _report.Step("Device selection", "FAIL", "ScanUSB returned zero devices.");
                    return 1;
                }

                string serial = ResolveSerial(serials, _options.SerialNumber);
                _report.Step("Device selection", "PASS", "Selected serial: " + serial);

                handle = OpenUsbDevice(comObject, serial);
                _report.Step("OpenUSBDevice", handle != 0 ? "PASS" : "FAIL", "Handle: " + handle.ToString(CultureInfo.InvariantCulture));
                if (handle == 0)
                {
                    return 1;
                }

                channel = ResolveChannel(comObject, handle);
                _report.Step("Sensor detection", channel >= 0 ? "PASS" : "FAIL", channel >= 0 ? "Selected channel: " + channel.ToString(CultureInfo.InvariantCulture) : "No active channels 0-3.");
                if (channel < 0)
                {
                    return 1;
                }

                Invoke(comObject, "StartStream", handle, channel);
                streaming = true;
                _report.Step("StartStream", "PASS", "Started channel " + channel.ToString(CultureInfo.InvariantCulture));

                ProbeSamples(comObject, handle, channel);
                return 0;
            }
            finally
            {
                if (comObject != null)
                {
                    if (streaming && handle != 0 && channel >= 0)
                    {
                        TryInvoke(comObject, "StopStream", handle, channel);
                        _report.Step("StopStream", "INFO", "StopStream was requested.");
                    }

                    if (handle != 0)
                    {
                        TryInvoke(comObject, "Close", handle);
                        _report.Step("Close", "INFO", "Close was requested.");
                    }

                    TryInvoke(comObject, "CloseAll");

                    if (Marshal.IsComObject(comObject))
                    {
                        Marshal.FinalReleaseComObject(comObject);
                    }
                }
            }
        }

        private void ProbeSamples(object comObject, int handle, int channel)
        {
            int pollCount = 0;
            int rawSampleCount = 0;
            int acceptedSampleCount = 0;
            int nonZeroStatusCount = 0;
            double? minEnergy = null;
            double? maxEnergy = null;
            List<string> statusPreview = new List<string>();
            DateTime deadline = DateTime.UtcNow + _options.Duration;

            while (DateTime.UtcNow < deadline)
            {
                pollCount++;
                object[] args = { handle, channel, null, null, null };
                Invoke(comObject, "GetData", args);
                double[] energies = args[2] as double[];
                double[] timestamps = args[3] as double[];
                int[] statuses = args[4] as int[];
                if (energies == null || timestamps == null || statuses == null)
                {
                    throw new InvalidOperationException("GetData returned an unexpected payload.");
                }

                for (int i = 0; i < energies.Length; i++)
                {
                    rawSampleCount++;
                    int rawStatus = statuses.Length > i ? statuses[i] : 0;
                    int measurementType = rawStatus / 0x10000;
                    int statusCode = rawStatus % 0x10000;
                    bool accepted = measurementType == 0 && statusCode == 0;
                    if (accepted)
                    {
                        acceptedSampleCount++;
                        minEnergy = !minEnergy.HasValue ? energies[i] : Math.Min(minEnergy.Value, energies[i]);
                        maxEnergy = !maxEnergy.HasValue ? energies[i] : Math.Max(maxEnergy.Value, energies[i]);
                    }
                    else
                    {
                        nonZeroStatusCount++;
                    }

                    if (statusPreview.Count < 12)
                    {
                        statusPreview.Add(rawStatus.ToString(CultureInfo.InvariantCulture) + " (type=" + measurementType.ToString(CultureInfo.InvariantCulture) + ", status=" + statusCode.ToString(CultureInfo.InvariantCulture) + ")");
                    }
                }

                Thread.Sleep(_options.PollInterval);
            }

            _report.Step("GetData", "PASS", "Polling completed.");
            _report.AppendLine("Poll count: " + pollCount.ToString(CultureInfo.InvariantCulture));
            _report.AppendLine("Raw samples observed: " + rawSampleCount.ToString(CultureInfo.InvariantCulture));
            _report.AppendLine("Accepted energy samples: " + acceptedSampleCount.ToString(CultureInfo.InvariantCulture));
            _report.AppendLine("Non-zero status samples: " + nonZeroStatusCount.ToString(CultureInfo.InvariantCulture));
            _report.AppendLine("Energy min/max: " + (minEnergy.HasValue && maxEnergy.HasValue ? minEnergy.Value.ToString("G17", CultureInfo.InvariantCulture) + " / " + maxEnergy.Value.ToString("G17", CultureInfo.InvariantCulture) : "n/a"));
            _report.AppendLine("First raw statuses: " + (statusPreview.Count > 0 ? string.Join(", ", statusPreview) : "n/a"));
            if (rawSampleCount == 0)
            {
                _report.AppendLine("No pulse samples were received. This is expected for pulse-triggered sensors if no laser pulse occurred during the probe window.");
            }
        }

        private string[] ScanUsb(object comObject, string stepName)
        {
            object[] args = { null };
            Invoke(comObject, "ScanUSB", args);
            Array array = args[0] as Array;
            string[] serials = ToStringArray(array);
            _report.Step(stepName, serials.Length > 0 ? "PASS" : "SKIPPED", "Detected devices: " + serials.Length.ToString(CultureInfo.InvariantCulture));
            _report.AppendLine(stepName + " serials: " + (serials.Length > 0 ? string.Join(", ", serials) : "none"));
            return serials;
        }

        private int OpenUsbDevice(object comObject, string serial)
        {
            object[] args = { serial, null };
            Invoke(comObject, "OpenUSBDevice", args);
            return Convert.ToInt32(args[1], CultureInfo.InvariantCulture);
        }

        private int ResolveChannel(object comObject, int handle)
        {
            for (int channel = 0; channel < 4; channel++)
            {
                object[] args = { handle, channel, null };
                Invoke(comObject, "IsSensorExists", args);
                bool exists = Convert.ToBoolean(args[2], CultureInfo.InvariantCulture);
                _report.AppendLine("IsSensorExists channel " + channel.ToString(CultureInfo.InvariantCulture) + ": " + FormatBool(exists));
                if (exists)
                {
                    return channel;
                }
            }

            return -1;
        }

        private static string ResolveSerial(string[] serials, string requested)
        {
            if (!string.IsNullOrWhiteSpace(requested))
            {
                for (int i = 0; i < serials.Length; i++)
                {
                    if (string.Equals(serials[i], requested, StringComparison.OrdinalIgnoreCase))
                    {
                        return serials[i];
                    }
                }

                throw new InvalidOperationException("Configured serial was not returned by ScanUSB: " + requested);
            }

            return serials[0];
        }

        private void AppendVersion(object comObject, string methodName, string label)
        {
            try
            {
                object[] args = { null };
                Invoke(comObject, methodName, args);
                _report.AppendLine(label + ": " + Convert.ToString(args[0], CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                _report.AppendLine(label + ": unavailable (" + ex.Message + ")");
            }
        }

        private void AppendComPackageDiagnostic(Type runtimeType)
        {
            string clsid = ResolveClsid(runtimeType);
            string dllPath = ResolveInprocServerPath(clsid);
            _report.AppendLine("COM CLSID: " + (string.IsNullOrWhiteSpace(clsid) ? "unavailable" : clsid));
            _report.AppendLine("COM DLL path: " + (string.IsNullOrWhiteSpace(dllPath) ? "unavailable" : dllPath));
            if (!string.IsNullOrWhiteSpace(dllPath))
            {
                _report.AppendLine("COM DLL exists: " + FormatBool(File.Exists(dllPath)));
                if (File.Exists(dllPath))
                {
                    FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(dllPath);
                    _report.AppendLine("COM DLL file version: " + (!string.IsNullOrWhiteSpace(versionInfo.FileVersion) ? versionInfo.FileVersion : versionInfo.ProductVersion));
                }

                string directory = Path.GetDirectoryName(dllPath);
                string firmwareDirectory = !string.IsNullOrWhiteSpace(directory) ? Path.Combine(directory, "firmware") : string.Empty;
                _report.AppendLine("Pulsar firmware directory: " + (string.IsNullOrWhiteSpace(firmwareDirectory) ? "unavailable" : firmwareDirectory));
                _report.AppendLine("Pulsar firmware directory exists: " + FormatBool(!string.IsNullOrWhiteSpace(firmwareDirectory) && Directory.Exists(firmwareDirectory)));
                AppendFirmware(firmwareDirectory, "FU4A*.hex");
                AppendFirmware(firmwareDirectory, "FU4B*.hex");
                AppendFirmware(firmwareDirectory, "FU4F*.ttf");
            }

            _report.AppendLine();
        }

        private void AppendFirmware(string firmwareDirectory, string pattern)
        {
            if (string.IsNullOrWhiteSpace(firmwareDirectory) || !Directory.Exists(firmwareDirectory))
            {
                _report.AppendLine("Pulsar firmware " + pattern + ": missing");
                return;
            }

            string[] paths = Directory.GetFiles(firmwareDirectory, pattern);
            string[] names = new string[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                names[i] = Path.GetFileName(paths[i]);
            }

            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            _report.AppendLine("Pulsar firmware " + pattern + ": " + (names.Length > 0 ? string.Join(", ", names) : "missing"));
        }

        private static string ResolveClsid(Type runtimeType)
        {
            using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(ProgramConstants.ProgId + "\\CLSID"))
            {
                object value = key != null ? key.GetValue(null) : null;
                string clsid = value as string;
                if (!string.IsNullOrWhiteSpace(clsid))
                {
                    return clsid.Trim();
                }
            }

            Guid guid = runtimeType != null ? runtimeType.GUID : Guid.Empty;
            return guid == Guid.Empty ? string.Empty : "{" + guid.ToString("D") + "}";
        }

        private static string ResolveInprocServerPath(string clsid)
        {
            if (string.IsNullOrWhiteSpace(clsid))
            {
                return string.Empty;
            }

            using (RegistryKey key = Registry.ClassesRoot.OpenSubKey("CLSID\\" + clsid.Trim() + "\\InprocServer32"))
            {
                object value = key != null ? key.GetValue(null) : null;
                string path = value as string;
                return string.IsNullOrWhiteSpace(path)
                    ? string.Empty
                    : Environment.ExpandEnvironmentVariables(path.Trim('"'));
            }
        }

        private static object Invoke(object comObject, string methodName, params object[] args)
        {
            try
            {
                return comObject.GetType().InvokeMember(
                    methodName,
                    BindingFlags.InvokeMethod,
                    null,
                    comObject,
                    args,
                    CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException ex)
            {
                throw CreateInvocationException(methodName, ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                throw CreateInvocationException(methodName, ex);
            }
        }

        private static void TryInvoke(object comObject, string methodName, params object[] args)
        {
            try
            {
                Invoke(comObject, methodName, args);
            }
            catch
            {
            }
        }

        private static Exception CreateInvocationException(string methodName, Exception ex)
        {
            return new InvalidOperationException(
                "OphirLMMeasurement call failed for '" + methodName + "'." + Environment.NewLine +
                BuildExceptionDetails(ex),
                ex);
        }

        private static string BuildExceptionDetails(Exception ex)
        {
            StringBuilder builder = new StringBuilder();
            Exception current = ex;
            int depth = 0;
            while (current != null && depth < 8)
            {
                if (depth > 0)
                {
                    builder.Append(" -> ");
                }

                builder.Append(current.GetType().FullName);
                builder.Append(": ");
                builder.Append(current.Message);
                COMException comException = current as COMException;
                if (comException != null)
                {
                    builder.Append(" (HRESULT=0x");
                    builder.Append(comException.ErrorCode.ToString("X8", CultureInfo.InvariantCulture));
                    builder.Append(")");
                }

                current = current.InnerException;
                depth++;
            }

            return builder.ToString();
        }

        private static string[] ToStringArray(Array array)
        {
            if (array == null || array.Length == 0)
            {
                return new string[0];
            }

            List<string> values = new List<string>();
            for (int i = 0; i < array.Length; i++)
            {
                string value = Convert.ToString(array.GetValue(i), CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }

            return values.ToArray();
        }

        private static string FormatBool(bool value)
        {
            return value ? "yes" : "no";
        }
    }

    internal static class ProgramConstants
    {
        public const string ProgId = "OphirLMMeasurement.CoLMMeasurement";
    }

    internal sealed class ProbeReport
    {
        private readonly StringBuilder _builder = new StringBuilder();

        public void Step(string name, string status, string details)
        {
            AppendLine("[" + status + "] " + name + " - " + details);
        }

        public void AppendLine()
        {
            _builder.AppendLine();
        }

        public void AppendLine(string value)
        {
            _builder.AppendLine(value);
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }

    internal sealed class ProbeOptions
    {
        private ProbeOptions()
        {
            Duration = TimeSpan.FromSeconds(10);
            PollInterval = TimeSpan.FromMilliseconds(50);
            ResetDelay = TimeSpan.FromSeconds(2);
            ResetRetryEnabled = true;
        }

        public string SerialNumber { get; private set; }

        public TimeSpan Duration { get; private set; }

        public TimeSpan PollInterval { get; private set; }

        public TimeSpan ResetDelay { get; private set; }

        public bool ResetRetryEnabled { get; private set; }

        public static bool TryParse(string[] args, out ProbeOptions options)
        {
            options = new ProbeOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(arg, "--serial", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        return false;
                    }

                    options.SerialNumber = args[++i];
                    continue;
                }

                if (string.Equals(arg, "--seconds", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadPositiveDouble(args, ref i, out double seconds))
                    {
                        return false;
                    }

                    options.Duration = TimeSpan.FromSeconds(seconds);
                    continue;
                }

                if (string.Equals(arg, "--poll-ms", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadPositiveDouble(args, ref i, out double milliseconds))
                    {
                        return false;
                    }

                    options.PollInterval = TimeSpan.FromMilliseconds(milliseconds);
                    continue;
                }

                if (string.Equals(arg, "--reset-delay-ms", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadPositiveDouble(args, ref i, out double milliseconds))
                    {
                        return false;
                    }

                    options.ResetDelay = TimeSpan.FromMilliseconds(milliseconds);
                    continue;
                }

                if (string.Equals(arg, "--no-reset-retry", StringComparison.OrdinalIgnoreCase))
                {
                    options.ResetRetryEnabled = false;
                    continue;
                }

                return false;
            }

            return true;
        }

        public static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  LaserEnergyMonitor.OphirComProbe.exe [--seconds 10] [--serial SERIAL] [--poll-ms 50] [--no-reset-retry]");
            Console.WriteLine();
            Console.WriteLine("Default flow:");
            Console.WriteLine("  new OphirLMMeasurement.CoLMMeasurement()");
            Console.WriteLine("  GetVersion / GetDriverVersion");
            Console.WriteLine("  ScanUSB");
            Console.WriteLine("  if empty: CloseAll, ResetAllDevices, wait, ScanUSB again");
            Console.WriteLine("  OpenUSBDevice, IsSensorExists, StartStream, poll GetData, StopStream, Close");
            Console.WriteLine();
            Console.WriteLine("For pulse-triggered sensors, fire at least one safe test pulse during the probe window.");
        }

        private static bool TryReadPositiveDouble(string[] args, ref int index, out double value)
        {
            value = 0d;
            if (index + 1 >= args.Length)
            {
                return false;
            }

            index++;
            return double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0d;
        }
    }
}
