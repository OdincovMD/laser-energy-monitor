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
                    ShowGui = false,
                    TimestampStrategy = BeamGageTimestampStrategy.HostArrivalUtc
                };
            }
        }

        public string AutomationInstanceId { get; set; }

        public bool ShowGui { get; set; }

        public string DataSource { get; set; }

        public string PowerMeter { get; set; }

        public string WaveLength { get; set; }

        public BeamGageTimestampStrategy TimestampStrategy { get; set; }
    }
}
