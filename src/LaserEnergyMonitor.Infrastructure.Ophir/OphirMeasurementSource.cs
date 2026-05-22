using System;
using System.Threading;
using System.Threading.Tasks;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public sealed class OphirMeasurementSource : IMeasurementSource
    {
        private readonly object _gate = new object();
        private readonly OphirMeasurementOptions _options;
        private CancellationTokenSource _cts;
        private Task _pollingTask;
        private OphirRuntimeSession _session;
        private OphirCaptureWriter _captureWriter;
        private long _sequence;
        private bool _streaming;
        private bool _disposed;

        public OphirMeasurementSource()
            : this(null)
        {
        }

        public OphirMeasurementSource(OphirMeasurementOptions options)
        {
            _options = options ?? OphirMeasurementOptions.Default;
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

                _session = OphirRuntimeSession.Open(_options);
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
                    throw new InvalidOperationException("Ophir source is not initialized.");
                }

                if (_streaming)
                {
                    return;
                }

                EnsureCaptureWriter();
                _session.StartStream();
                _cts = new CancellationTokenSource();
                _pollingTask = Task.Factory.StartNew(
                    () => PollMeasurements(_cts.Token),
                    _cts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                _streaming = true;
            }
        }

        public void Stop()
        {
            CancellationTokenSource cts = null;
            Task pollingTask = null;
            bool canWaitForTask = true;

            lock (_gate)
            {
                cts = _cts;
                pollingTask = _pollingTask;
                _cts = null;
                _pollingTask = null;
                _streaming = false;
                canWaitForTask = pollingTask == null || !Task.CurrentId.HasValue || pollingTask.Id != Task.CurrentId.Value;

                if (cts != null)
                {
                    cts.Cancel();
                }
            }

            if (pollingTask != null && canWaitForTask)
            {
                try
                {
                    pollingTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }

            if (cts != null)
            {
                cts.Dispose();
            }

            lock (_gate)
            {
                if (_session != null)
                {
                    _session.StopStream();
                }

                DisposeCaptureWriter();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;

            lock (_gate)
            {
                if (_session != null)
                {
                    _session.Dispose();
                    _session = null;
                }

                IsConnected = false;
            }
        }

        private void PollMeasurements(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    OphirDataBatch batch;
                    lock (_gate)
                    {
                        if (_session == null)
                        {
                            break;
                        }

                        batch = _session.GetDataBatch();
                    }

                    if (batch.Count > 0)
                    {
                        PublishSamples(batch);
                    }

                    if (token.WaitHandle.WaitOne(_options.PollInterval))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
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
                                Message = "Ophir streaming failed: " + ex.Message,
                                TimestampUtc = DateTime.UtcNow,
                                Exception = ex
                            }));
                    break;
                }
            }
        }

        private void PublishSamples(OphirDataBatch batch)
        {
            int sampleCount = batch.Count;
            for (int i = 0; i < sampleCount; i++)
            {
                int rawStatus = batch.Statuses[i];
                int measurementType = rawStatus / 0x10000;
                int status = rawStatus % 0x10000;
                DateTime publishedUtc = ResolveTimestampUtc(batch.RecordedUtc, batch.Timestamps[i]);
                long sequenceNumber = Interlocked.Increment(ref _sequence);

                OphirCaptureWriter captureWriter = _captureWriter;
                if (captureWriter != null)
                {
                    captureWriter.Append(sequenceNumber, batch.RecordedUtc, publishedUtc, batch.Timestamps[i], rawStatus, batch.Energies[i]);
                }

                if (measurementType != 0 || status != 0)
                {
                    continue;
                }

                MeasurementReceived?.Invoke(
                    this,
                        new MeasurementReceivedEventArgs(
                        new MeasurementSample
                        {
                            SourceId = SourceId,
                            SequenceNumber = sequenceNumber,
                            TimestampUtc = publishedUtc,
                            MonotonicTicks = batch.RecordedUtc.Ticks,
                            Energy = batch.Energies[i]
                        }));
            }
        }

        private DateTime ResolveTimestampUtc(DateTime recordedUtc, double vendorTimestampSeconds)
        {
            if (_options.TimestampStrategy == OphirTimestampStrategy.VendorSecondsFromLocalMidnight)
            {
                return ConvertVendorTimestampToUtc(recordedUtc, vendorTimestampSeconds);
            }

            return recordedUtc;
        }

        private static DateTime ConvertVendorTimestampToUtc(DateTime recordedUtc, double vendorTimestampSeconds)
        {
            if (vendorTimestampSeconds <= 0)
            {
                return recordedUtc;
            }

            try
            {
                DateTime localDate = recordedUtc.ToLocalTime().Date;
                return DateTime.SpecifyKind(localDate.AddSeconds(vendorTimestampSeconds), DateTimeKind.Local).ToUniversalTime();
            }
            catch
            {
                return recordedUtc;
            }
        }

        private void EnsureCaptureWriter()
        {
            if (_captureWriter != null || string.IsNullOrWhiteSpace(_options.CaptureDirectoryPath))
            {
                return;
            }

            _captureWriter = new OphirCaptureWriter(_options.CaptureDirectoryPath);
        }

        private void DisposeCaptureWriter()
        {
            if (_captureWriter == null)
            {
                return;
            }

            _captureWriter.Dispose();
            _captureWriter = null;
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
