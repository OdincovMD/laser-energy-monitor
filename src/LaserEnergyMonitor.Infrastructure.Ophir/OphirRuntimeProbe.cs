using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public static class OphirRuntimeProbe
    {
        private const string ProgId = "OphirLMMeasurement.CoLMMeasurement";

        public static MeasurementSourceRuntimeProbeResult Probe()
        {
            List<MeasurementSourceRuntimeProbeStep> steps = new List<MeasurementSourceRuntimeProbeStep>();
            Type runtimeType = Type.GetTypeFromProgID(ProgId, false);
            if (runtimeType == null)
            {
                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = false,
                    Summary = "Ophir COM runtime is not registered.",
                    Steps = new[]
                    {
                        new MeasurementSourceRuntimeProbeStep
                        {
                            Name = "COM registration",
                            Status = "FAIL",
                            Details = "ProgID '" + ProgId + "' was not found."
                        }
                    },
                    Details =
                        "The ProgID '" + ProgId + "' was not found. " +
                        "Install the Ophir COM runtime or vendor automation package."
                };
            }

            steps.Add(new MeasurementSourceRuntimeProbeStep
            {
                Name = "COM registration",
                Status = "PASS",
                Details = "ProgID detected: " + ProgId
            });

            object comObject = null;
            try
            {
                comObject = Activator.CreateInstance(runtimeType);
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "COM activation",
                    Status = "PASS",
                    Details = "COM type: " + runtimeType.FullName
                });

                StringBuilder details = new StringBuilder();
                details.Append("ProgID detected: ");
                details.Append(ProgId);
                details.AppendLine();
                details.Append("COM type: ");
                details.Append(runtimeType.FullName);
                details.AppendLine();
                AppendDriverVersion(comObject, details);
                details.Append("Required methods: ");
                details.Append(DescribeMethodAvailability(runtimeType));
                details.AppendLine();

                Array serialNumbers = ScanUsb(comObject);
                int deviceCount = serialNumbers != null ? serialNumbers.Length : 0;
                details.Append("ScanUSB result count: ");
                details.Append(deviceCount.ToString(CultureInfo.InvariantCulture));
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "USB scan",
                    Status = deviceCount > 0 ? "PASS" : "SKIPPED",
                    Details = "Detected devices: " + deviceCount.ToString(CultureInfo.InvariantCulture)
                });

                if (deviceCount == 0)
                {
                    details.AppendLine();
                    details.Append(
                        "Compatibility hint: if a Pulsar device is visible in Ophir software but ScanUSB stays empty, " +
                        "use the separate Ophir Pulsar ActiveX (legacy) source backed by OphirFastX.");
                    steps.Add(new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Device open",
                        Status = "SKIPPED",
                        Details = "No USB devices were returned by ScanUSB."
                    });
                    steps.Add(new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Sensor detection",
                        Status = "SKIPPED",
                        Details = "No opened device is available."
                    });
                    steps.Add(new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Stream probe",
                        Status = "SKIPPED",
                        Details = "No opened device is available."
                    });
                    steps.Add(new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Legacy Pulsar API",
                        Status = "INFO",
                        Details = "Pulsar devices may require OphirFastX ActiveX instead of OphirLMMeasurement.ScanUSB."
                    });

                    return new MeasurementSourceRuntimeProbeResult
                    {
                        DependencyAvailable = true,
                        Summary = "Ophir COM runtime is functional. No USB devices are currently visible.",
                        Steps = steps,
                        Details = details.ToString()
                    };
                }

                details.AppendLine();
                details.Append("First detected device: ");
                string firstDevice = Convert.ToString(serialNumbers.GetValue(0), CultureInfo.InvariantCulture);
                details.Append(firstDevice);
                details.AppendLine();

                int handle = OpenUsbDevice(comObject, firstDevice);
                details.Append("OpenUSBDevice handle: ");
                details.Append(handle.ToString(CultureInfo.InvariantCulture));
                details.AppendLine();
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "Device open",
                    Status = handle != 0 ? "PASS" : "FAIL",
                    Details = "OpenUSBDevice handle: " + handle.ToString(CultureInfo.InvariantCulture)
                });

                List<int> activeChannels = GetActiveChannels(comObject, handle);
                int streamChannel = activeChannels.Count > 0 ? activeChannels[0] : -1;
                details.Append("Active sensor channels: ");
                details.Append(activeChannels.Count > 0 ? string.Join(", ", activeChannels) : "none");
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "Sensor detection",
                    Status = activeChannels.Count > 0 ? "PASS" : "FAIL",
                    Details = activeChannels.Count > 0
                        ? "Detected active sensor channel(s): " + string.Join(", ", activeChannels)
                        : "No active sensor was detected on channels 0-3."
                });

                if (streamChannel >= 0)
                {
                    details.AppendLine();
                    details.Append("Stream probe: ");
                    string streamDetails = TryStreamProbe(comObject, handle, streamChannel);
                    details.Append(streamDetails);
                    steps.Add(new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Stream probe",
                        Status = "PASS",
                        Details = streamDetails
                    });
                }
                else
                {
                    steps.Add(new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Stream probe",
                        Status = "SKIPPED",
                        Details = "No active sensor was detected on channels 0-3."
                    });
                }

                TryCloseDevice(comObject, handle);

                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = true,
                    Summary = "Ophir COM runtime is functional and USB devices were detected.",
                    Steps = steps,
                    Details = details.ToString()
                };
            }
            catch (Exception ex)
            {
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "Self-check failure",
                    Status = "FAIL",
                    Details = BuildExceptionDetails(ex)
                });

                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = false,
                    Summary = "Ophir COM runtime is registered, but the self-check failed.",
                    Steps = steps,
                    Details = BuildExceptionDetails(ex)
                };
            }
            finally
            {
                if (comObject != null && Marshal.IsComObject(comObject))
                {
                    Marshal.FinalReleaseComObject(comObject);
                }
            }
        }

        private static Array ScanUsb(object comObject)
        {
            object[] args = { null };
            Invoke(comObject, "ScanUSB", args);
            return args[0] as Array;
        }

        private static int OpenUsbDevice(object comObject, string serialNumber)
        {
            object[] args = { serialNumber, null };
            Invoke(comObject, "OpenUSBDevice", args);
            return Convert.ToInt32(args[1], CultureInfo.InvariantCulture);
        }

        private static void AppendDriverVersion(object comObject, StringBuilder details)
        {
            try
            {
                object[] args = { null };
                Invoke(comObject, "GetDriverVersion", args);
                string version = Convert.ToString(args[0], CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    details.Append("Driver version: ");
                    details.Append(version);
                    details.AppendLine();
                }
            }
            catch
            {
            }
        }

        private static bool IsSensorExists(object comObject, int handle, int channel)
        {
            object[] args = { handle, channel, null };
            Invoke(comObject, "IsSensorExists", args);
            return Convert.ToBoolean(args[2], CultureInfo.InvariantCulture);
        }

        private static List<int> GetActiveChannels(object comObject, int handle)
        {
            List<int> activeChannels = new List<int>();
            for (int channel = 0; channel < 4; channel++)
            {
                if (IsSensorExists(comObject, handle, channel))
                {
                    activeChannels.Add(channel);
                }
            }

            return activeChannels;
        }

        private static string TryStreamProbe(object comObject, int handle, int channel)
        {
            try
            {
                Invoke(comObject, "StartStream", handle, channel);
                System.Threading.Thread.Sleep(250);
                object[] data = GetData(comObject, handle, channel);
                Array samples = data[0] as Array;
                int sampleCount = samples != null ? samples.Length : 0;
                return "StartStream/GetData/StopStream ok, sample count=" + sampleCount.ToString(CultureInfo.InvariantCulture);
            }
            finally
            {
                try
                {
                    Invoke(comObject, "StopStream", handle, channel);
                }
                catch
                {
                }
            }
        }

        private static object[] GetData(object comObject, int handle, int channel)
        {
            object[] args = { handle, channel, null, null, null };
            Invoke(comObject, "GetData", args);
            return new[] { args[2], args[3], args[4] };
        }

        private static void TryCloseDevice(object comObject, int handle)
        {
            try
            {
                Invoke(comObject, "Close", handle);
            }
            catch
            {
            }
        }

        private static object Invoke(object comObject, string methodName, params object[] args)
        {
            return comObject.GetType().InvokeMember(
                methodName,
                BindingFlags.InvokeMethod,
                null,
                comObject,
                args,
                CultureInfo.InvariantCulture);
        }

        private static string DescribeMethodAvailability(Type runtimeType)
        {
            string[] methods =
            {
                "ScanUSB",
                "OpenUSBDevice",
                "IsSensorExists",
                "StartStream",
                "StopStream",
                "GetData"
            };

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < methods.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(methods[i]);
                builder.Append("=");
                builder.Append(runtimeType.GetMethod(methods[i]) != null ? "yes" : "late-bound");
            }

            return builder.ToString();
        }

        private static string BuildExceptionDetails(Exception ex)
        {
            StringBuilder builder = new StringBuilder();
            Exception current = ex;
            int depth = 0;

            while (current != null && depth < 5)
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
}
