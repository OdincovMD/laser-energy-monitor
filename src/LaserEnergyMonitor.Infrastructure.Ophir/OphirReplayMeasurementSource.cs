using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public sealed class OphirReplayMeasurementSource : IMeasurementSource
    {
        private readonly object _gate = new object();
        private readonly OphirMeasurementOptions _options;
        private List<ReplaySample> _samples;
        private CancellationTokenSource _cts;
        private Task _replayTask;
        private bool _disposed;

        public OphirReplayMeasurementSource(OphirMeasurementOptions options)
        {
            _options = options ?? throw new ArgumentNullException("options");
        }

        public string SourceId
        {
            get { return "Ophir"; }
        }

        public bool IsConnected { get; private set; }

        public event EventHandler<MeasurementReceivedEventArgs> MeasurementReceived;
        public event EventHandler<DeviceFaultEventArgs> Faulted;

        public void Initialize()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (IsConnected)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(_options.ReplayFilePath))
                {
                    throw new InvalidOperationException("Ophir replay file path is not configured.");
                }

                if (!File.Exists(_options.ReplayFilePath))
                {
                    throw new FileNotFoundException("Ophir replay file was not found.", _options.ReplayFilePath);
                }

                _samples = LoadSamples(_options.ReplayFilePath);
                if (_samples.Count == 0)
                {
                    throw new InvalidOperationException("Ophir replay file does not contain any samples.");
                }

                IsConnected = true;
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!IsConnected || _samples == null)
                {
                    throw new InvalidOperationException("Ophir replay source is not initialized.");
                }

                if (_replayTask != null && !_replayTask.IsCompleted)
                {
                    return;
                }

                _cts = new CancellationTokenSource();
                _replayTask = Task.Factory.StartNew(
                    () => ReplayLoop(_cts.Token),
                    _cts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
        }

        public void Stop()
        {
            CancellationTokenSource cts = null;
            Task replayTask = null;

            lock (_gate)
            {
                cts = _cts;
                replayTask = _replayTask;
                _cts = null;
                _replayTask = null;

                if (cts != null)
                {
                    cts.Cancel();
                }
            }

            if (replayTask != null)
            {
                try
                {
                    replayTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException)
                {
                }
            }

            if (cts != null)
            {
                cts.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            _samples = null;
            IsConnected = false;
        }

        private void ReplayLoop(CancellationToken token)
        {
            try
            {
                DateTime? previousPublishedUtc = null;
                double speedMultiplier = _options.ReplaySpeedMultiplier > 0 ? _options.ReplaySpeedMultiplier : 1.0d;

                for (int i = 0; i < _samples.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    ReplaySample sample = _samples[i];

                    if (previousPublishedUtc.HasValue)
                    {
                        TimeSpan originalDelay = sample.PublishedUtc - previousPublishedUtc.Value;
                        if (originalDelay < TimeSpan.Zero)
                        {
                            originalDelay = TimeSpan.Zero;
                        }

                        long scaledTicks = (long)(originalDelay.Ticks / speedMultiplier);
                        if (scaledTicks > TimeSpan.FromSeconds(2).Ticks)
                        {
                            scaledTicks = TimeSpan.FromSeconds(2).Ticks;
                        }

                        if (scaledTicks > 0 && token.WaitHandle.WaitOne(TimeSpan.FromTicks(scaledTicks)))
                        {
                            break;
                        }
                    }

                    previousPublishedUtc = sample.PublishedUtc;
                    MeasurementReceived?.Invoke(
                        this,
                        new MeasurementReceivedEventArgs(
                            new MeasurementSample
                            {
                                SourceId = SourceId,
                                SequenceNumber = sample.SequenceNumber,
                                TimestampUtc = sample.PublishedUtc,
                                MonotonicTicks = sample.PublishedUtc.Ticks,
                                Energy = sample.Energy
                            }));
                }
            }
            catch (OperationCanceledException)
            {
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
                            Message = "Ophir replay failed: " + ex.Message,
                            TimestampUtc = DateTime.UtcNow,
                            Exception = ex
                        }));
            }
        }

        private static List<ReplaySample> LoadSamples(string replayFilePath)
        {
            List<ReplaySample> samples = new List<ReplaySample>();
            string[] lines = File.ReadAllLines(replayFilePath);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split(',');
                if (parts.Length < 8)
                {
                    continue;
                }

                int statusCode;
                if (!int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out statusCode) || statusCode != 0)
                {
                    continue;
                }

                long sequenceNumber;
                DateTime publishedUtc;
                double energy;
                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequenceNumber) ||
                    !DateTime.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out publishedUtc) ||
                    !double.TryParse(parts[7], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out energy))
                {
                    continue;
                }

                samples.Add(
                    new ReplaySample
                    {
                        SequenceNumber = sequenceNumber,
                        PublishedUtc = publishedUtc,
                        Energy = energy
                    });
            }

            return samples;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private sealed class ReplaySample
        {
            public long SequenceNumber { get; set; }

            public DateTime PublishedUtc { get; set; }

            public double Energy { get; set; }
        }
    }
}
