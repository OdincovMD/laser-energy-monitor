using System;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure
{
    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }
    }
}
