using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
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
        private Task _pollerTask;
        private long _lastFrameReceivedUtcTicks;
        private long _sequence;
        private int _streamingFaultReported;
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

        public string CurrentEnergyUnitBase
        {
            get { return _session != null ? _session.CurrentEnergyUnitBase : string.Empty; }
        }

        public string CurrentEnergyUnitQuantifier
        {
            get { return _session != null ? _session.CurrentEnergyUnitQuantifier : string.Empty; }
        }

        public double? CurrentScaleMultiplier
        {
            get { return _session != null ? _session.CurrentScaleMultiplier : null; }
        }

        public string CurrentDataSourceStatus
        {
            get { return _session != null ? _session.DataSourceStatus : string.Empty; }
        }

        public bool IsSourceOnline
        {
            get { return _session != null && _session.IsOnline; }
        }

        public IReadOnlyList<string> AvailablePhysicalDataSources
        {
            get
            {
                lock (_gate)
                {
                    return _session != null
                        ? _session.GetAvailablePhysicalDataSources()
                        : new string[0];
                }
            }
        }

        public IReadOnlyList<string> AvailableDataSources
        {
            get
            {
                lock (_gate)
                {
                    return _session != null
                        ? _session.GetAvailableDataSources()
                        : new string[0];
                }
            }
        }

        public long OnNewFrameCallbackCount
        {
            get { return _session != null ? _session.OnNewFrameCallbackCount : 0L; }
        }

        public long EventFrameCount
        {
            get { return _session != null ? _session.EventFrameCount : 0L; }
        }

        public long PollFrameAttemptCount
        {
            get { return _session != null ? _session.PollFrameAttemptCount : 0L; }
        }

        public long PolledFrameCount
        {
            get { return _session != null ? _session.PolledFrameCount : 0L; }
        }

        public long DuplicateFrameSkipCount
        {
            get { return _session != null ? _session.DuplicateFrameSkipCount : 0L; }
        }

        public long LastObservedFrameId
        {
            get { return _session != null ? _session.LastObservedFrameId : 0L; }
        }

        public string LastFrameReadError
        {
            get { return _session != null ? _session.LastFrameReadError : string.Empty; }
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
                Interlocked.Exchange(ref _lastFrameReceivedUtcTicks, DateTime.UtcNow.Ticks);
                Interlocked.Exchange(ref _streamingFaultReported, 0);
                _streaming = true;
                _publisherTask = Task.Factory.StartNew(
                    () => PublishMeasurements(_queue, _cts.Token),
                    _cts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                try
                {
                    _session.StartStream();
                    if (_options.PollingFallbackEnabled)
                    {
                        _pollerTask = Task.Factory.StartNew(
                            () => PollMeasurements(_queue, _cts.Token),
                            _cts.Token,
                            TaskCreationOptions.LongRunning,
                            TaskScheduler.Default);
                    }
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
            Task pollerTask = null;
            bool canWaitForTask = true;

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
                pollerTask = _pollerTask;
                _queue = null;
                _cts = null;
                _publisherTask = null;
                _pollerTask = null;
                canWaitForTask = publisherTask == null ||
                    !Task.CurrentId.HasValue ||
                    publisherTask.Id != Task.CurrentId.Value;
                canWaitForTask = canWaitForTask &&
                    (pollerTask == null ||
                    !Task.CurrentId.HasValue ||
                    pollerTask.Id != Task.CurrentId.Value);
            }

            Exception stopException = null;
            try
            {
                if (_session != null)
                {
                    _session.StopStream();
                }
            }
            catch (Exception ex)
            {
                stopException = ex;
            }
            finally
            {
                if (cts != null)
                {
                    cts.Cancel();
                }

                if (queue != null)
                {
                    queue.CompleteAdding();
                }

                if (pollerTask != null && canWaitForTask)
                {
                    try
                    {
                        pollerTask.Wait(TimeSpan.FromSeconds(2));
                    }
                    catch (AggregateException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                if (publisherTask != null && canWaitForTask)
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

            if (stopException != null)
            {
                ExceptionDispatchInfo.Capture(stopException).Throw();
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
            if (e == null)
            {
                return;
            }

            QueueSnapshot(e.Snapshot);
        }

        private void QueueSnapshot(BeamGageFrameSnapshot snapshot)
        {
            BlockingCollection<BeamGageFrameSnapshot> queue = _queue;
            if (!_streaming || queue == null || snapshot == null)
            {
                return;
            }

            try
            {
                Interlocked.Exchange(ref _lastFrameReceivedUtcTicks, snapshot.RecordedUtc.Ticks);
                queue.Add(snapshot);
            }
            catch (ObjectDisposedException)
            {
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

            ReportStreamingFault(e.Message, e.Exception);
        }

        private void PollMeasurements(BlockingCollection<BeamGageFrameSnapshot> queue, CancellationToken token)
        {
            try
            {
                TimeSpan interval = GetEffectivePollingFallbackInterval();
                while (!token.IsCancellationRequested && !queue.IsCompleted)
                {
                    BeamGageRuntimeSession session = _session;
                    if (session == null)
                    {
                        break;
                    }

                    BeamGageFrameSnapshot snapshot = session.TryPollFrame();
                    if (snapshot != null)
                    {
                        QueueSnapshot(snapshot);
                    }

                    if (token.WaitHandle.WaitOne(interval))
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                ReportStreamingFault("BeamGage polling fallback failed: " + ex.Message, ex);
            }
        }

        private void PublishMeasurements(BlockingCollection<BeamGageFrameSnapshot> queue, CancellationToken token)
        {
            try
            {
                TimeSpan frameTimeout = GetEffectiveFrameTimeout();
                TimeSpan watchdogInterval = GetWatchdogInterval(frameTimeout);
                while (!token.IsCancellationRequested)
                {
                    BeamGageFrameSnapshot snapshot;
                    if (!queue.TryTake(out snapshot, (int)watchdogInterval.TotalMilliseconds, token))
                    {
                        if (queue.IsCompleted)
                        {
                            break;
                        }

                        DateTime lastFrameReceivedUtc = new DateTime(
                            Interlocked.Read(ref _lastFrameReceivedUtcTicks),
                            DateTimeKind.Utc);
                        if (DateTime.UtcNow - lastFrameReceivedUtc >= frameTimeout)
                        {
                            ReportStreamingFault(
                                "BeamGage physical data source stopped delivering new frames for " +
                                frameTimeout.TotalSeconds.ToString("0.###") +
                                " seconds: " + CurrentDataSource,
                                null);
                            break;
                        }

                        continue;
                    }

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
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                ReportStreamingFault("BeamGage publishing failed: " + ex.Message, ex);
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

        private void ReportStreamingFault(string message, Exception exception)
        {
            if (Interlocked.CompareExchange(ref _streamingFaultReported, 1, 0) != 0)
            {
                return;
            }

            IsConnected = false;
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
                AllowBuiltInDataSources = options.AllowBuiltInDataSources,
                PowerMeter = options.PowerMeter,
                WaveLength = options.WaveLength,
                TimestampStrategy = options.TimestampStrategy,
                FrameTimeout = options.FrameTimeout,
                PollingFallbackEnabled = options.PollingFallbackEnabled,
                PollingFallbackInterval = options.PollingFallbackInterval,
                ResetPowerEnergyCalibrationOnStart = options.ResetPowerEnergyCalibrationOnStart,
                PowerEnergyCalibrationValue = options.PowerEnergyCalibrationValue,
                PowerEnergyCalibrationUnitBase = options.PowerEnergyCalibrationUnitBase,
                PowerEnergyCalibrationUnitQuantifier = options.PowerEnergyCalibrationUnitQuantifier
            };
        }

        private TimeSpan GetEffectiveFrameTimeout()
        {
            return _options.FrameTimeout > TimeSpan.Zero
                ? _options.FrameTimeout
                : BeamGageMeasurementOptions.Default.FrameTimeout;
        }

        private static TimeSpan GetWatchdogInterval(TimeSpan frameTimeout)
        {
            double intervalMs = frameTimeout.TotalMilliseconds / 4d;
            intervalMs = Math.Max(100d, Math.Min(1000d, intervalMs));
            return TimeSpan.FromMilliseconds(intervalMs);
        }

        private TimeSpan GetEffectivePollingFallbackInterval()
        {
            if (_options.PollingFallbackInterval > TimeSpan.Zero)
            {
                return _options.PollingFallbackInterval;
            }

            return BeamGageMeasurementOptions.Default.PollingFallbackInterval;
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
