using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.BeamGage
{
    public sealed class BeamGageMeasurementSource : IMeasurementSource
    {
        private readonly object _gate = new object();
        private readonly BeamGageMeasurementOptions _options;
        private BeamGageRuntimeSession _session;
        private BlockingCollection<BeamGageFrameSnapshot> _queue;
        private CancellationTokenSource _cts;
        private Task _publisherTask;
        private long _sequence;
        private bool _streaming;
        private bool _disposed;

        public BeamGageMeasurementSource()
            : this(null)
        {
        }

        public BeamGageMeasurementSource(BeamGageMeasurementOptions options)
        {
            _options = options ?? BeamGageMeasurementOptions.Default;
        }

        public string SourceId
        {
            get { return "BeamGage"; }
        }

        public bool IsConnected { get; private set; }

        public string CurrentDataSource
        {
            get { return _session != null ? _session.CurrentDataSource : string.Empty; }
        }

        public string CurrentPowerMeter
        {
            get { return _session != null ? _session.CurrentPowerMeter : string.Empty; }
        }

        public string CurrentWaveLength
        {
            get { return _session != null ? _session.CurrentWaveLength : string.Empty; }
        }

        public string CurrentDataSourceStatus
        {
            get { return _session != null ? _session.DataSourceStatus : string.Empty; }
        }

        public bool IsSourceOnline
        {
            get { return _session != null && _session.IsOnline; }
        }

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

                _session = BeamGageRuntimeSession.Open(CloneOptions(_options));
                _session.FrameAvailable += OnFrameAvailable;
                _session.Faulted += OnSessionFaulted;
                IsConnected = true;
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!IsConnected || _session == null)
                {
                    throw new InvalidOperationException("BeamGage source is not initialized.");
                }

                if (_streaming)
                {
                    return;
                }

                _queue = new BlockingCollection<BeamGageFrameSnapshot>(new ConcurrentQueue<BeamGageFrameSnapshot>());
                _cts = new CancellationTokenSource();
                _streaming = true;
                _publisherTask = Task.Factory.StartNew(
                    () => PublishMeasurements(_queue, _cts.Token),
                    _cts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                try
                {
                    _session.StartStream();
                }
                catch
                {
                    _streaming = false;
                    _queue.CompleteAdding();
                    _cts.Cancel();
                    throw;
                }
            }
        }

        public void Stop()
        {
            BlockingCollection<BeamGageFrameSnapshot> queue = null;
            CancellationTokenSource cts = null;
            Task publisherTask = null;

            lock (_gate)
            {
                if (!_streaming)
                {
                    return;
                }

                _streaming = false;
                queue = _queue;
                cts = _cts;
                publisherTask = _publisherTask;
                _queue = null;
                _cts = null;
                _publisherTask = null;
            }

            if (_session != null)
            {
                _session.StopStream();
            }

            if (cts != null)
            {
                cts.Cancel();
            }

            if (queue != null)
            {
                queue.CompleteAdding();
            }

            if (publisherTask != null)
            {
                try
                {
                    publisherTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }

            if (queue != null)
            {
                queue.Dispose();
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

            lock (_gate)
            {
                if (_session != null)
                {
                    _session.FrameAvailable -= OnFrameAvailable;
                    _session.Faulted -= OnSessionFaulted;
                    _session.Dispose();
                    _session = null;
                }

                IsConnected = false;
            }
        }

        private void OnFrameAvailable(object sender, BeamGageFrameAvailableEventArgs e)
        {
            BlockingCollection<BeamGageFrameSnapshot> queue = _queue;
            if (!_streaming || queue == null || e == null || e.Snapshot == null)
            {
                return;
            }

            try
            {
                queue.Add(e.Snapshot);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OnSessionFaulted(object sender, BeamGageFaultEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            RaiseFault(e.Message, e.Exception);
        }

        private void PublishMeasurements(BlockingCollection<BeamGageFrameSnapshot> queue, CancellationToken token)
        {
            try
            {
                foreach (BeamGageFrameSnapshot snapshot in queue.GetConsumingEnumerable(token))
                {
                    MeasurementReceived?.Invoke(
                        this,
                        new MeasurementReceivedEventArgs(
                            new MeasurementSample
                            {
                                SourceId = SourceId,
                                SequenceNumber = ResolveSequence(snapshot.FrameId),
                                TimestampUtc = snapshot.TimestampUtc,
                                MonotonicTicks = snapshot.RecordedUtc.Ticks,
                                Energy = snapshot.Energy
                            }));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RaiseFault("BeamGage publishing failed: " + ex.Message, ex);
            }
        }

        private long ResolveSequence(long vendorFrameId)
        {
            while (true)
            {
                long current = Interlocked.Read(ref _sequence);
                long candidate = vendorFrameId > current ? vendorFrameId : current + 1L;
                if (Interlocked.CompareExchange(ref _sequence, candidate, current) == current)
                {
                    return candidate;
                }
            }
        }

        private void RaiseFault(string message, Exception exception)
        {
            Faulted?.Invoke(
                this,
                new DeviceFaultEventArgs(
                    new DeviceFault
                    {
                        SourceId = SourceId,
                        Severity = FaultSeverity.Critical,
                        Message = message,
                        TimestampUtc = DateTime.UtcNow,
                        Exception = exception
                    }));
        }

        private static BeamGageMeasurementOptions CloneOptions(BeamGageMeasurementOptions options)
        {
            return new BeamGageMeasurementOptions
            {
                AutomationInstanceId = options.AutomationInstanceId,
                ShowGui = options.ShowGui,
                DataSource = options.DataSource,
                PowerMeter = options.PowerMeter,
                WaveLength = options.WaveLength,
                TimestampStrategy = options.TimestampStrategy
            };
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
