using System;
using System.Threading;
using System.Threading.Tasks;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure
{
    public sealed class SimulatedMeasurementSource : IMeasurementSource
    {
        private readonly object _gate = new object();
        private readonly Random _random;
        private readonly double _baseEnergy;
        private readonly int _intervalMs;
        private CancellationTokenSource _cts;
        private Task _worker;
        private long _sequence;

        public SimulatedMeasurementSource(string sourceId, double baseEnergy, int intervalMs)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException("Source id is required.", "sourceId");
            }

            SourceId = sourceId;
            _baseEnergy = baseEnergy;
            _intervalMs = intervalMs;
            _random = new Random(sourceId.GetHashCode());
        }

        public string SourceId { get; private set; }

        public bool IsConnected { get; private set; }

        public event EventHandler<MeasurementReceivedEventArgs> MeasurementReceived;
        public event EventHandler<DeviceFaultEventArgs> Faulted;

        public void Initialize()
        {
            IsConnected = true;
        }

        public void Start()
        {
            lock (_gate)
            {
                if (!IsConnected)
                {
                    throw new InvalidOperationException("Source is not initialized.");
                }

                if (_worker != null && !_worker.IsCompleted)
                {
                    return;
                }

                _cts = new CancellationTokenSource();
                _worker = Task.Factory.StartNew(
                    () => ProduceMeasurements(_cts.Token),
                    _cts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                }
            }
        }

        public void Dispose()
        {
            Stop();
            if (_cts != null)
            {
                _cts.Dispose();
            }
        }

        private void ProduceMeasurements(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    long next = Interlocked.Increment(ref _sequence);
                    double energy = ComputeEnergy(next);
                    MeasurementReceived?.Invoke(
                        this,
                        new MeasurementReceivedEventArgs(
                            new MeasurementSample
                            {
                                SourceId = SourceId,
                                SequenceNumber = next,
                                TimestampUtc = DateTime.UtcNow,
                                MonotonicTicks = DateTime.UtcNow.Ticks,
                                Energy = energy
                            }));

                    Thread.Sleep(_intervalMs);
                }
                catch (Exception ex)
                {
                    Faulted?.Invoke(
                        this,
                        new DeviceFaultEventArgs(
                            new DeviceFault
                            {
                                SourceId = SourceId,
                                Severity = FaultSeverity.Critical,
                                Message = "Simulation source failed.",
                                TimestampUtc = DateTime.UtcNow,
                                Exception = ex
                            }));

                    break;
                }
            }
        }

        private double ComputeEnergy(long sequence)
        {
            double noise = (_random.NextDouble() - 0.5d) * 0.04d;

            if (sequence < 50)
            {
                return _baseEnergy + (sequence * 0.01d) + noise;
            }

            if (sequence < 180)
            {
                return _baseEnergy + 0.5d + noise;
            }

            if (sequence < 230)
            {
                return _baseEnergy + 1.2d + noise;
            }

            return _baseEnergy + 0.55d + noise;
        }
    }
}
