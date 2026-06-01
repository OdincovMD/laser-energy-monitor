using System;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    internal interface IOphirRuntimeSession : IDisposable
    {
        string SerialNumber { get; }

        int Channel { get; }

        void StartStream();

        void StopStream();

        OphirDataBatch GetDataBatch();
    }
}
