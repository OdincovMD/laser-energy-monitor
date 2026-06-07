using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public static class OphirFastXSimulationRuntimeProbe
    {
        public static MeasurementSourceRuntimeProbeResult Probe()
        {
            using (StaWorker worker = new StaWorker("Simulated OphirFastX probe worker"))
            {
                return worker.Invoke(ProbeOnSta);
            }
        }

        private static MeasurementSourceRuntimeProbeResult ProbeOnSta()
        {
            object comObject = new OphirFastXSimulatedActiveX();
            List<MeasurementSourceRuntimeProbeStep> steps = new List<MeasurementSourceRuntimeProbeStep>();
            StringBuilder details = new StringBuilder();

            try
            {
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "ActiveX simulation",
                    Status = "PASS",
                    Details = "In-process fake object exposes the OphirFastX method surface."
                });

                details.AppendLine("Activation mode: Simulated OphirFastX ActiveX object");
                details.AppendLine("API family: legacy OphirFastX ActiveX for Pulsar devices");
                details.AppendLine("Required methods: OpenUSB, GetNumberOfDevices, GetDeviceHandle, IsChannelExists, StartCS2, GetData, StopCS, CloseUSB");

                int openUsbWarning = OphirFastXRuntimeSession.OpenUsb(comObject);
                details.Append("OpenUSB warning code: ");
                details.AppendLine(openUsbWarning.ToString(CultureInfo.InvariantCulture));
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "USB open",
                    Status = "PASS",
                    Details = "OpenUSB completed against simulated ActiveX. Warning code: " +
                        openUsbWarning.ToString(CultureInfo.InvariantCulture)
                });

                List<OphirFastXRuntimeSession.DeviceDescriptor> devices = OphirFastXRuntimeSession.GetDevices(comObject);
                details.Append("Pulsar device count: ");
                details.AppendLine(devices.Count.ToString(CultureInfo.InvariantCulture));
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "Pulsar scan",
                    Status = devices.Count > 0 ? "PASS" : "FAIL",
                    Details = "Detected simulated devices: " + devices.Count.ToString(CultureInfo.InvariantCulture)
                });

                OphirFastXRuntimeSession.DeviceDescriptor device = devices[0];
                details.Append("Simulated device: handle=");
                details.Append(device.Handle.ToString(CultureInfo.InvariantCulture));
                details.Append(", serial=");
                details.AppendLine(device.SerialNumber);

                List<int> channels = OphirFastXRuntimeSession.GetActiveChannels(comObject, device.Handle);
                details.Append("Active sensor channels: ");
                details.AppendLine(channels.Count > 0 ? string.Join(", ", channels) : "none");
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "Sensor detection",
                    Status = channels.Count > 0 ? "PASS" : "FAIL",
                    Details = channels.Count > 0
                        ? "Detected active simulated channel(s): " + string.Join(", ", channels)
                        : "No simulated channel was detected."
                });

                int sampleCount = TryStreamProbe(comObject, device.Handle, Convert.ToInt16(channels[0], CultureInfo.InvariantCulture));
                details.Append("Stream probe sample count: ");
                details.AppendLine(sampleCount.ToString(CultureInfo.InvariantCulture));
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "Stream probe",
                    Status = "PASS",
                    Details = "StartCS2/GetData/StopCS completed, sample count=" + sampleCount.ToString(CultureInfo.InvariantCulture)
                });

                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = true,
                    Summary = "Simulated Ophir Pulsar ActiveX runtime is ready.",
                    Steps = steps,
                    Details = details.ToString()
                };
            }
            catch (Exception ex)
            {
                steps.Add(new MeasurementSourceRuntimeProbeStep
                {
                    Name = "Simulation failure",
                    Status = "FAIL",
                    Details = ex.Message
                });

                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = false,
                    Summary = "Simulated Ophir Pulsar ActiveX runtime failed.",
                    Steps = steps,
                    Details = ex.ToString()
                };
            }
            finally
            {
                OphirFastXRuntimeSession.TryInvoke(comObject, "CloseUSB");
            }
        }

        private static int TryStreamProbe(object comObject, short handle, short channel)
        {
            try
            {
                OphirFastXRuntimeSession.EnsureSuccess(comObject, "EnableDisableChannelForCS", handle, channel, (short)1);
                OphirFastXRuntimeSession.EnsureSuccess(comObject, "StartCS2", handle);
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
