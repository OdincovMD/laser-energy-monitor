using System;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public sealed class OphirMeasurementOptions
    {
        public static OphirMeasurementOptions Default
        {
            get
            {
                return new OphirMeasurementOptions
                {
                    PollInterval = TimeSpan.FromMilliseconds(50),
                    PreferredChannel = null,
                    TimestampStrategy = OphirTimestampStrategy.HostArrivalUtc
                };
            }
        }

        public string DeviceSerialNumber { get; set; }

        public int? PreferredChannel { get; set; }

        public TimeSpan PollInterval { get; set; }

        public OphirTimestampStrategy TimestampStrategy { get; set; }

        public string CaptureDirectoryPath { get; set; }

        public string ReplayFilePath { get; set; }

        public double ReplaySpeedMultiplier { get; set; }
    }
}
