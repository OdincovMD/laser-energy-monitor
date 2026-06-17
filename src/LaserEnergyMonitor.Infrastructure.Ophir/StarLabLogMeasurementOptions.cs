using System;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public sealed class StarLabLogMeasurementOptions
    {
        public static StarLabLogMeasurementOptions Default
        {
            get
            {
                return new StarLabLogMeasurementOptions
                {
                    LogFilePath = string.Empty,
                    EnergyColumnName = "Math M",
                    PollInterval = TimeSpan.FromSeconds(1),
                    StartAtEnd = true
                };
            }
        }

        public string LogFilePath { get; set; }

        public string EnergyColumnName { get; set; }

        public TimeSpan PollInterval { get; set; }

        public bool StartAtEnd { get; set; }
    }
}
