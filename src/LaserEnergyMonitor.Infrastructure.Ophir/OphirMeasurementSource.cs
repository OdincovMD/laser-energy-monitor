using System;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public sealed class OphirMeasurementSource : IMeasurementSource
    {
        public string SourceId
        {
            get { return "Ophir"; }
        }

        public bool IsConnected { get; private set; }

        public event EventHandler<MeasurementReceivedEventArgs> MeasurementReceived;
        public event EventHandler<DeviceFaultEventArgs> Faulted;

        public void Initialize()
        {
            throw new NotSupportedException("Ophir / Pulsar-4 integration spike is not connected yet.");
        }

        public void Start()
        {
            throw new NotSupportedException("Ophir / Pulsar-4 integration spike is not connected yet.");
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
