using System;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.BeamGage
{
    public sealed class BeamGageMeasurementSource : IMeasurementSource
    {
        public string SourceId
        {
            get { return "BeamGage"; }
        }

        public bool IsConnected { get; private set; }

        public event EventHandler<MeasurementReceivedEventArgs> MeasurementReceived;
        public event EventHandler<DeviceFaultEventArgs> Faulted;

        public void Initialize()
        {
            throw new NotSupportedException("Beam Gage integration spike is not connected yet.");
        }

        public void Start()
        {
            throw new NotSupportedException("Beam Gage integration spike is not connected yet.");
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
