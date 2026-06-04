using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using Microsoft.Win32;

namespace LaserEnergyMonitor.Wpf
{
    internal static class UsbDeviceInventory
    {
        private static readonly string[] InterestingNeedles =
        {
            "ophir",
            "pulsar",
            "jungo",
            "windriver",
            "windrvr",
            "starlab"
        };

        public static string BuildReport(DateTime generatedLocalTime)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("USB inventory generated at ");
            builder.AppendLine(generatedLocalTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            builder.Append("Machine: ");
            builder.AppendLine(Environment.MachineName);
            builder.Append("User: ");
            builder.AppendLine(Environment.UserName);
            builder.Append("OS: ");
            builder.AppendLine(Environment.OSVersion.ToString());
            builder.AppendLine();

            List<UsbDeviceInfo> devices = new List<UsbDeviceInfo>();
            Exception wmiException = null;
            try
            {
                devices.AddRange(ReadFromWmi());
                builder.Append("Source: ");
                builder.AppendLine("WMI Win32_PnPEntity");
            }
            catch (Exception ex)
            {
                wmiException = ex;
                builder.Append("Source: ");
                builder.AppendLine("Registry fallback");
                builder.Append("WMI error: ");
                builder.AppendLine(ex.Message);
                devices.AddRange(ReadFromRegistry());
            }

            devices = devices
                .OrderBy(device => device.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(device => device.DeviceId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<UsbDeviceInfo> interesting = devices
                .Where(IsInteresting)
                .ToList();

            builder.Append("USB device count: ");
            builder.AppendLine(devices.Count.ToString(CultureInfo.InvariantCulture));
            builder.Append("Likely relevant count: ");
            builder.AppendLine(interesting.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();

            builder.AppendLine("Likely Ophir / Pulsar / driver matches:");
            AppendDevices(builder, interesting);

            builder.AppendLine();
            builder.AppendLine("All USB / USBSTOR devices:");
            AppendDevices(builder, devices);

            if (wmiException != null)
            {
                builder.AppendLine();
                builder.AppendLine("WMI diagnostic details:");
                builder.AppendLine(wmiException.ToString());
            }

            return builder.ToString().Trim();
        }

        private static IEnumerable<UsbDeviceInfo> ReadFromWmi()
        {
            List<UsbDeviceInfo> devices = new List<UsbDeviceInfo>();
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, PNPDeviceID, Manufacturer, Service, Status, ClassGuid FROM Win32_PnPEntity"))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        string deviceId = ReadWmiString(item, "PNPDeviceID");
                        if (string.IsNullOrWhiteSpace(deviceId))
                        {
                            deviceId = ReadWmiString(item, "DeviceID");
                        }

                        if (!IsUsbDeviceId(deviceId))
                        {
                            continue;
                        }

                        devices.Add(new UsbDeviceInfo
                        {
                            Name = ReadWmiString(item, "Name"),
                            DeviceId = deviceId,
                            Manufacturer = ReadWmiString(item, "Manufacturer"),
                            Service = ReadWmiString(item, "Service"),
                            Status = ReadWmiString(item, "Status"),
                            ClassGuid = ReadWmiString(item, "ClassGuid")
                        });
                    }
                }
            }

            return devices;
        }

        private static IEnumerable<UsbDeviceInfo> ReadFromRegistry()
        {
            List<UsbDeviceInfo> devices = new List<UsbDeviceInfo>();
            ReadRegistryBranch(devices, "SYSTEM\\CurrentControlSet\\Enum\\USB", "USB");
            ReadRegistryBranch(devices, "SYSTEM\\CurrentControlSet\\Enum\\USBSTOR", "USBSTOR");
            return devices;
        }

        private static void ReadRegistryBranch(List<UsbDeviceInfo> devices, string path, string prefix)
        {
            using (RegistryKey branch = Registry.LocalMachine.OpenSubKey(path))
            {
                if (branch == null)
                {
                    return;
                }

                foreach (string deviceKeyName in branch.GetSubKeyNames())
                {
                    using (RegistryKey deviceKey = branch.OpenSubKey(deviceKeyName))
                    {
                        if (deviceKey == null)
                        {
                            continue;
                        }

                        foreach (string instanceName in deviceKey.GetSubKeyNames())
                        {
                            using (RegistryKey instanceKey = deviceKey.OpenSubKey(instanceName))
                            {
                                if (instanceKey == null)
                                {
                                    continue;
                                }

                                string friendlyName = ReadRegistryString(instanceKey, "FriendlyName");
                                string deviceDesc = ReadRegistryString(instanceKey, "DeviceDesc");
                                devices.Add(new UsbDeviceInfo
                                {
                                    Name = !string.IsNullOrWhiteSpace(friendlyName) ? friendlyName : StripRegistryResourcePrefix(deviceDesc),
                                    DeviceId = prefix + "\\" + deviceKeyName + "\\" + instanceName,
                                    Manufacturer = StripRegistryResourcePrefix(ReadRegistryString(instanceKey, "Mfg")),
                                    Service = ReadRegistryString(instanceKey, "Service"),
                                    Status = "registry",
                                    ClassGuid = ReadRegistryString(instanceKey, "ClassGUID")
                                });
                            }
                        }
                    }
                }
            }
        }

        private static void AppendDevices(StringBuilder builder, IReadOnlyList<UsbDeviceInfo> devices)
        {
            if (devices.Count == 0)
            {
                builder.AppendLine("  n/a");
                return;
            }

            for (int i = 0; i < devices.Count; i++)
            {
                UsbDeviceInfo device = devices[i];
                builder.Append("  ");
                builder.Append((i + 1).ToString(CultureInfo.InvariantCulture));
                builder.Append(". ");
                builder.AppendLine(string.IsNullOrWhiteSpace(device.Name) ? "(no name)" : device.Name);
                AppendField(builder, "DeviceId", device.DeviceId);
                AppendField(builder, "Manufacturer", device.Manufacturer);
                AppendField(builder, "Service", device.Service);
                AppendField(builder, "Status", device.Status);
                AppendField(builder, "ClassGuid", device.ClassGuid);
            }
        }

        private static void AppendField(StringBuilder builder, string name, string value)
        {
            builder.Append("     ");
            builder.Append(name);
            builder.Append(": ");
            builder.AppendLine(string.IsNullOrWhiteSpace(value) ? "n/a" : value);
        }

        private static bool IsInteresting(UsbDeviceInfo device)
        {
            string haystack = string.Join(
                " ",
                new[]
                {
                    device.Name,
                    device.DeviceId,
                    device.Manufacturer,
                    device.Service,
                    device.Status,
                    device.ClassGuid
                }.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();

            for (int i = 0; i < InterestingNeedles.Length; i++)
            {
                if (haystack.Contains(InterestingNeedles[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUsbDeviceId(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return false;
            }

            return deviceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
                deviceId.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadWmiString(ManagementBaseObject item, string propertyName)
        {
            try
            {
                object value = item[propertyName];
                return value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ReadRegistryString(RegistryKey key, string valueName)
        {
            object value = key.GetValue(valueName);
            return value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string StripRegistryResourcePrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            int separatorIndex = value.LastIndexOf(';');
            return separatorIndex >= 0 && separatorIndex < value.Length - 1
                ? value.Substring(separatorIndex + 1)
                : value;
        }

        private sealed class UsbDeviceInfo
        {
            public string Name { get; set; }

            public string DeviceId { get; set; }

            public string Manufacturer { get; set; }

            public string Service { get; set; }

            public string Status { get; set; }

            public string ClassGuid { get; set; }
        }
    }
}
