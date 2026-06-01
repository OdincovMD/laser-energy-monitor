using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public static class OphirFastXRuntimeProbe
    {
        public static MeasurementSourceRuntimeProbeResult Probe()
        {
            string progId;
            Type runtimeType = OphirFastXRuntimeSession.FindRuntimeType(out progId);
            if (runtimeType == null)
            {
                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = false,
                    Summary = "Ophir Pulsar ActiveX runtime is not registered.",
                    Steps = new[]
                    {
                        new MeasurementSourceRuntimeProbeStep
                        {
                            Name = "ActiveX registration",
                            Status = "FAIL",
                            Details = "Expected ProgID OPHIRFASTX.OphirFastXCtrl.1 or OPHIRFASTXBeta.OphirFastXCtrl.1."
                        }
                    },
                    Details =
                        "Install and register the x86 OphirFastX ActiveX control supplied by Ophir. " +
                        "This legacy runtime is required for Pulsar devices that are visible in Ophir software but are not returned by OphirLMMeasurement.ScanUSB."
                };
            }

            using (StaWorker worker = new StaWorker("OphirFastX probe worker"))
            {
                return worker.Invoke(
                    delegate
                    {
                        return ProbeOnSta(runtimeType, progId);
                    });
            }
        }

        private static MeasurementSourceRuntimeProbeResult ProbeOnSta(Type runtimeType, string progId)
        {
            List<MeasurementSourceRuntimeProbeStep> steps = new List<MeasurementSourceRuntimeProbeStep>();
            steps.Add(new MeasurementSourceRuntimeProbeStep
            {
                Name = "ActiveX registration",
                Status = "PASS",
                Details = "ProgID detected: " + progId
            });

            object comObject = null;
            bool usbOpened = false;
            try
            {
                comObject = OphirFastXRuntimeSession.CreateRuntimeInstance();
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "ActiveX activation",
                    Status = "PASS",
                    Details = "COM type: " + runtimeType.FullName
                });

                StringBuilder details = new StringBuilder();
                details.Append("ProgID detected: ");
                details.AppendLine(progId);
                details.Append("COM type: ");
                details.AppendLine(runtimeType.FullName);
                details.AppendLine("API family: legacy OphirFastX ActiveX for Pulsar devices");
                details.AppendLine("Required methods: OpenUSB, GetNumberOfDevices, GetDeviceHandle, IsChannelExists, StartCS2, GetData, StopCS, CloseUSB");

                OphirFastXRuntimeSession.OpenUsb(comObject);
                usbOpened = true;
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "USB open",
                    Status = "PASS",
                    Details = "OpenUSB completed."
                });

                List<OphirFastXRuntimeSession.DeviceDescriptor> devices = OphirFastXRuntimeSession.GetDevices(comObject);
                details.Append("Pulsar device count: ");
                details.AppendLine(devices.Count.ToString(CultureInfo.InvariantCulture));
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "Pulsar scan",
                    Status = devices.Count > 0 ? "PASS" : "SKIPPED",
                    Details = "Detected devices: " + devices.Count.ToString(CultureInfo.InvariantCulture)
                });

                if (devices.Count == 0)
                {
                    steps.Add(new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Sensor detection",
                        Status = "SKIPPED",
                        Details = "No opened Pulsar device is available."
                    });
                    steps.Add(new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Stream probe",
                        Status = "SKIPPED",
                        Details = "No opened Pulsar device is available."
                    });

                    return new MeasurementSourceRuntimeProbeResult
                    {
                        DependencyAvailable = true,
                        Summary = "Ophir Pulsar ActiveX runtime is functional. No Pulsar USB devices are currently visible.",
                        Steps = steps,
                        Details = details.ToString()
                    };
                }

                OphirFastXRuntimeSession.DeviceDescriptor device = devices[0];
                details.Append("First detected device: handle=");
                details.Append(device.Handle.ToString(CultureInfo.InvariantCulture));
                details.Append(", serial=");
                details.Append(string.IsNullOrWhiteSpace(device.SerialNumber) ? "n/a" : device.SerialNumber);
                details.Append(", name=");
                details.AppendLine(string.IsNullOrWhiteSpace(device.Name) ? "n/a" : device.Name);

                List<int> channels = OphirFastXRuntimeSession.GetActiveChannels(comObject, device.Handle);
                details.Append("Active sensor channels: ");
                details.AppendLine(channels.Count > 0 ? string.Join(", ", channels) : "none");
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "Sensor detection",
                    Status = channels.Count > 0 ? "PASS" : "FAIL",
                    Details = channels.Count > 0
                        ? "Detected active sensor channel(s): " + string.Join(", ", channels)
                        : "No active sensor was detected on channels 0-3."
                });

                if (channels.Count > 0)
                {
                    int sampleCount = TryStreamProbe(comObject, device.Handle, Convert.ToInt16(channels[0], CultureInfo.InvariantCulture));
                    details.Append("Stream probe sample count: ");
                    details.AppendLine(sampleCount.ToString(CultureInfo.InvariantCulture));
                    steps.Add(new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Stream probe",
                        Status = "PASS",
                        Details = "StartCS2/GetData/StopCS completed, sample count=" + sampleCount.ToString(CultureInfo.InvariantCulture)
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

                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = true,
                    Summary = "Ophir Pulsar ActiveX runtime is functional and USB devices were detected.",
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
                    Details = ex.Message
                });

                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = false,
                    Summary = "Ophir Pulsar ActiveX runtime is registered, but the self-check failed.",
                    Steps = steps,
                    Details = ex.ToString()
                };
            }
            finally
            {
                if (usbOpened)
                {
                    OphirFastXRuntimeSession.TryInvoke(comObject, "CloseUSB");
                }

                OphirFastXRuntimeSession.ReleaseComObject(comObject);
            }
        }

        private static int TryStreamProbe(object comObject, short handle, short channel)
        {
            try
            {
                OphirFastXRuntimeSession.EnsureSuccess(comObject, "EnableDisableChannelForCS", handle, channel, (short)1);
                OphirFastXRuntimeSession.EnsureSuccess(comObject, "StartCS2", handle);
                System.Threading.Thread.Sleep(250);
                object[] args = { null };
                OphirFastXRuntimeSession.EnsureSuccess(comObject, "GetData", args);
                OphirDataBatch batch = OphirFastXRuntimeSession.ParseDataBatch(args[0] as Array, handle, channel, DateTime.UtcNow);
                return batch.Count;
            }
            finally
            {
                OphirFastXRuntimeSession.TryInvoke(comObject, "StopCS", handle);
            }
        }
    }
}
