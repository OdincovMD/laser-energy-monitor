using System;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public static class OphirRuntimeProbe
    {
        private const string ProgId = "OphirLMMeasurement.CoLMMeasurement";

        public static MeasurementSourceRuntimeProbeResult Probe()
        {
            Type runtimeType = Type.GetTypeFromProgID(ProgId, false);
            if (runtimeType != null)
            {
                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = true,
                    Summary = "Ophir COM runtime is registered.",
                    Details = "ProgID detected: " + ProgId
                };
            }

            return new MeasurementSourceRuntimeProbeResult
            {
                DependencyAvailable = false,
                Summary = "Ophir COM runtime is not registered.",
                Details =
                    "The ProgID '" + ProgId + "' was not found. " +
                    "Install the Ophir COM runtime or vendor automation package."
            };
        }
    }
}
