using System;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    internal sealed class OphirDataBatch
    {
        public OphirDataBatch(double[] energies, double[] timestamps, int[] statuses, DateTime recordedUtc)
        {
            Energies = energies ?? Array.Empty<double>();
            Timestamps = timestamps ?? Array.Empty<double>();
            Statuses = statuses ?? Array.Empty<int>();
            RecordedUtc = recordedUtc;
        }

        public double[] Energies { get; private set; }

        public double[] Timestamps { get; private set; }

        public int[] Statuses { get; private set; }

        public DateTime RecordedUtc { get; private set; }

        public int Count
        {
            get
            {
                return Math.Min(Energies.Length, Math.Min(Timestamps.Length, Statuses.Length));
            }
        }
    }
}
