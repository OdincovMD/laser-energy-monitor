using System;

namespace LaserEnergyMonitor.Infrastructure.BeamGage
{
    public sealed class BeamGageMeasurementOptions
    {
        public static BeamGageMeasurementOptions Default
        {
            get
            {
                return new BeamGageMeasurementOptions
                {
                    AutomationInstanceId = "LaserEnergyMonitor",
                    ShowGui = true,
                    AllowBuiltInDataSources = true,
                    TimestampStrategy = BeamGageTimestampStrategy.HostArrivalUtc,
                    FrameTimeout = TimeSpan.FromSeconds(10),
                    PollingFallbackEnabled = true,
                    PollingFallbackInterval = TimeSpan.FromMilliseconds(50)
                };
            }
        }

        public string AutomationInstanceId { get; set; }

        public bool ShowGui { get; set; }

        public string DataSource { get; set; }

        public bool AllowBuiltInDataSources { get; set; }

        public string PowerMeter { get; set; }

        public string WaveLength { get; set; }

        public BeamGageTimestampStrategy TimestampStrategy { get; set; }

        public TimeSpan FrameTimeout { get; set; }

        public bool PollingFallbackEnabled { get; set; }

        public TimeSpan PollingFallbackInterval { get; set; }

        public bool ResetPowerEnergyCalibrationOnStart { get; set; }

        public double? PowerEnergyCalibrationValue { get; set; }

        public string PowerEnergyCalibrationUnitBase { get; set; }

        public string PowerEnergyCalibrationUnitQuantifier { get; set; }
    }
}
